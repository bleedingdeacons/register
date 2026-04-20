using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// Controls the meeting lifecycle:
	///
	///   <b>Start Meeting</b>:
	///     1. Sync all data from Unity API → capture snapshot.
	///     2. User selects an intergroup meeting.
	///     3. App is ready for registrations.
	///
	///   <b>Finish Meeting</b>:
	///     1. Detect local changes against the snapshot.
	///     2. Push creates / updates / registrations to Unity API.
	///     3. Re-sync and re-snapshot.
	///
	/// Meeting state is tracked via <see cref="MeetingPhase"/>:
	///   NotStarted → Syncing → SelectMeeting → InProgress → Finishing → Completed
	/// </summary>
	public partial class AdminViewModel : BaseViewModel
	{
		private static readonly ILogger Logger = AppLogger.ForContext<AdminViewModel>();

		private readonly DataService _dataService;
		private readonly IIntergroupMeetingRepository _intergroupMeetingRepository;
		private readonly IConfigurationService _configService;
		private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;

		// ── Meeting phase ────────────────────────────────────────────
		public enum MeetingPhase
		{
			NotStarted,
			Syncing,
			SelectMeeting,
			InProgress,
			Finishing,
			Completed
		}

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsNotStarted))]
		[NotifyPropertyChangedFor(nameof(IsSyncing))]
		[NotifyPropertyChangedFor(nameof(IsSelectingMeeting))]
		[NotifyPropertyChangedFor(nameof(IsInProgress))]
		[NotifyPropertyChangedFor(nameof(IsFinishing))]
		[NotifyPropertyChangedFor(nameof(IsCompleted))]
		[NotifyPropertyChangedFor(nameof(IsBusy))]
		private MeetingPhase phase = MeetingPhase.NotStarted;

		public bool IsNotStarted => Phase == MeetingPhase.NotStarted;
		public bool IsSyncing => Phase == MeetingPhase.Syncing;
		public bool IsSelectingMeeting => Phase == MeetingPhase.SelectMeeting;
		public bool IsInProgress => Phase == MeetingPhase.InProgress;
		public bool IsFinishing => Phase == MeetingPhase.Finishing;
		public bool IsCompleted => Phase == MeetingPhase.Completed;
		public new bool IsBusy => Phase is MeetingPhase.Syncing or MeetingPhase.Finishing;

		// ── Status / progress ────────────────────────────────────────

		[ObservableProperty]
		private string statusMessage = string.Empty;

		[ObservableProperty]
		private bool isStatusError = false;

		[ObservableProperty]
		private bool isStatusVisible = false;

		// ── Sync error state ─────────────────────────────────────────

		[ObservableProperty]
		private bool hasSyncError = false;

		[ObservableProperty]
		private bool hasSyncedSuccessfully = false;

		[ObservableProperty]
		private bool hasFinishSyncError = false;

		// ── Active meeting ───────────────────────────────────────────

		[ObservableProperty]
		private int? activeMeetingId = null;

		[ObservableProperty]
		private string activeMeetingDate = string.Empty;

		[ObservableProperty]
		private string activeMeetingTitle = string.Empty;

		// ── Finish results ───────────────────────────────────────────

		[ObservableProperty]
		private string finishSummary = string.Empty;

		// ── Meeting list (shown during SelectMeeting phase) ──────────

		public ObservableCollection<IntergroupMeeting> Meetings { get; } = new();

		public AdminViewModel(
			DataService dataService,
			IIntergroupMeetingRepository intergroupMeetingRepository,
			IConfigurationService configService,
			IDbContextFactory<UnityDbContext> dbContextFactory)
		{
			_dataService = dataService;
			_intergroupMeetingRepository = intergroupMeetingRepository;
			_configService = configService;
			_dbContextFactory = dbContextFactory;

			// Restore phase if a meeting is already active (app restarted mid-session)
			RestorePhaseAsync().SafeFireAndForget("RestorePhase");
		}

		// =============================================================
		// Start Meeting
		// =============================================================

		/// <summary>
		/// Step 1: Sync from Unity and capture a snapshot, then show
		/// the meeting selection list.
		/// </summary>
		[RelayCommand]
		private async Task StartMeeting()
		{
			var config = await _configService.LoadUnityConfigurationAsync();
			if (!config.IsValid())
			{
				ShowStatus("Unity API not configured. Go to Settings → Unity API Settings first.", true);
				HasSyncError = true;
				return;
			}

			Logger.Information(
				"StartMeeting config — BaseUrl: {BaseUrl}, ApiKey: {ApiKeyStatus}, ActiveMeetingId: {ActiveMeetingId}",
				config.BaseUrl,
				string.IsNullOrEmpty(config.ApiKey) ? "(not set)" : "***",
				config.ActiveIntergroupMeetingId);

			try
			{
				Phase = MeetingPhase.Syncing;
				HasSyncError = false;
				ShowStatus("Syncing data from Unity...", false);

				var (meetings, positions, members, groups, contacts, intergroupMeetings) =
					await _dataService.ImportWithSnapshotAsync(Token);

				ShowStatus(
					$"Sync complete: {groups} groups, {meetings} meetings, " +
					$"{members} members, {positions} positions.",
					false);

				Logger.Information(
					"Start Meeting sync complete: {Groups} groups, {Meetings} meetings, {Members} members",
					groups, meetings, members);

				HasSyncedSuccessfully = true;

				// Load the intergroup meetings list for selection
				await LoadMeetingsAsync();

				Phase = MeetingPhase.SelectMeeting;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Start Meeting sync failed");
				ShowStatus($"Sync failed: {ex.Message}", true);
				HasSyncError = true;
				Phase = MeetingPhase.NotStarted;
			}
		}

		/// <summary>
		/// Retry sync after a previous failure.
		/// </summary>
		[RelayCommand]
		private async Task RetrySync()
		{
			await StartMeeting();
		}

		/// <summary>
		/// Step 2: User selects an intergroup meeting from the list.
		/// </summary>
		[RelayCommand]
		private async Task SelectMeeting(IntergroupMeeting meeting)
		{
			if (meeting == null) return;

			await _configService.SaveActiveIntergroupMeetingAsync(meeting.Id);

			// Reset all registered flags for the new session
			await ResetAllRegisteredStateAsync();

			ActiveMeetingId = meeting.Id;
			UpdateActiveMeetingDisplay(meeting);

			Phase = MeetingPhase.InProgress;
			HideStatus();

			Logger.Information(
				"Meeting started: ID {Id}, Title {Title}, Date {Date}",
				meeting.Id, meeting.Title, meeting.Date);

			// Navigate to the main page now that a meeting is active
			await Shell.Current.GoToAsync("//MainPage");
		}

		// =============================================================
		// Finish Meeting
		// =============================================================

		/// <summary>
		/// Pushes all local changes to Unity, re-syncs, and ends the session.
		/// </summary>
		[RelayCommand]
		private async Task FinishMeeting()
		{
			bool confirmed = await Shell.Current.DisplayAlert(
				"Finish Meeting",
				"All registrations and member changes will be pushed to Unity.\n\nAre you sure?",
				"Yes, Finish",
				"Cancel");

			if (!confirmed) return;

			Phase = MeetingPhase.Finishing;
			await ExecuteFinishReconciliationAsync();
		}

		/// <summary>
		/// Retries the finish-meeting sync after a previous failure.
		/// </summary>
		[RelayCommand]
		private async Task RetryFinish()
		{
			await ExecuteFinishReconciliationAsync();
		}

		/// <summary>
		/// Shared reconciliation logic for both FinishMeeting and RetryFinish.
		/// </summary>
		private async Task ExecuteFinishReconciliationAsync()
		{
			try
			{
				HasFinishSyncError = false;
				ShowStatus("Pushing changes to Unity...", false);

				var (meetings, positions, members, groups, contacts, intergroupMeetings,
					 created, modified, registered, errors, warnings) =
					await _dataService.ImportWithReconciliationAsync(Token);

				// If any non-recoverable errors occurred, stay in Finishing phase
				// so the user sees Retry Sync. Purge is only available after a
				// fully clean reconciliation. Note: "already registered" responses
				// from Unity are treated as success in ReconciliationService and
				// do not count as errors here.
				if (errors > 0)
				{
					HasFinishSyncError = true;
					ShowStatus(
						$"Reconciliation completed with {errors} error(s). Tap Retry Sync to try again.",
						true);
					Logger.Warning(
						"Finish Meeting reconciliation reported {Errors} errors — staying in Finishing phase",
						errors);
					return;
				}

				// Clear the active meeting
				await _configService.SaveActiveIntergroupMeetingAsync(null);
				ActiveMeetingId = null;
				ActiveMeetingDate = string.Empty;
				ActiveMeetingTitle = string.Empty;

				FinishSummary =
					$"Pushed to Unity:\n" +
					$"  • {created} new members created\n" +
					$"  • {modified} members updated\n" +
					$"  • {registered} registrations recorded\n" +
					(errors > 0 ? $"  • {errors} API errors\n" : "") +
					$"\nRe-synced: {groups} groups, {meetings} meetings, {members} members.";

				if (warnings > 0)
					Logger.Warning("Meeting finished with {Warnings} API warnings (non-fatal)", warnings);

				Phase = MeetingPhase.Completed;
				HideStatus();

				Logger.Information(
					"Meeting finished: {Created} created, {Modified} modified, {Registered} registered, {Errors} errors, {Warnings} warnings",
					created, modified, registered, errors, warnings);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Finish Meeting reconciliation failed");
				ShowStatus($"Reconciliation failed: {ex.Message}", true);
				HasFinishSyncError = true;
				// Remain at Finishing phase so the retry button is visible
			}
		}

		/// <summary>
		/// Purges all data from the local database and resets the session.
		/// </summary>
		[RelayCommand]
		private async Task PurgeDatabase()
		{
			bool confirmed = await Shell.Current.DisplayAlert(
				"Purge Database",
				"This will permanently delete ALL local data including groups, members, meetings, positions, and snapshots.\n\nThis action cannot be undone. Are you sure?",
				"Yes, Purge Everything",
				"No, Keep Data");

			if (!confirmed) return;

			try
			{
				ShowStatus("Purging database...", false);

				using var dbContext = _dbContextFactory.CreateDbContext();
				await dbContext.PurgeDatabaseAsync(Token);

				Phase = MeetingPhase.NotStarted;
				FinishSummary = string.Empty;
				Meetings.Clear();
				HideStatus();

				Logger.Information("Database purged successfully");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Database purge failed");
				ShowStatus($"Purge failed: {ex.Message}", true);
			}
		}

		// =============================================================
		// Private helpers
		// =============================================================

		private async Task RestorePhaseAsync()
		{
			try
			{
				var config = await _configService.LoadUnityConfigurationAsync();
				if (config.ActiveIntergroupMeetingId.HasValue)
				{
					ActiveMeetingId = config.ActiveIntergroupMeetingId;

					var meeting = await _intergroupMeetingRepository
						.GetByIdAsync(config.ActiveIntergroupMeetingId.Value, Token);

					if (meeting != null)
					{
						UpdateActiveMeetingDisplay(meeting);
						Phase = MeetingPhase.InProgress;
						Logger.Information("Restored active meeting session: {Id}", meeting.Id);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to restore meeting phase");
			}
		}

		private async Task LoadMeetingsAsync()
		{
			var meetings = await _intergroupMeetingRepository.GetAllAsync(Token);

			Meetings.Clear();
			foreach (var meeting in meetings)
				Meetings.Add(meeting);

			Logger.Information("Loaded {Count} intergroup meetings", meetings.Count);
		}

		private async Task ResetAllRegisteredStateAsync()
		{
			try
			{
				using var dbContext = _dbContextFactory.CreateDbContext();

				await dbContext.Groups
					.Where(g => g.Registered)
					.ExecuteUpdateAsync(s => s.SetProperty(g => g.Registered, false), Token);

				await dbContext.Positions
					.Where(p => p.Registered)
					.ExecuteUpdateAsync(s => s.SetProperty(p => p.Registered, false), Token);

				Logger.Information("Reset all Registered flags for new meeting session");
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to reset Registered flags");
			}
		}

		private void UpdateActiveMeetingDisplay(IntergroupMeeting meeting)
		{
			ActiveMeetingDate = meeting.Date ?? string.Empty;
			ActiveMeetingTitle = meeting.Title ?? string.Empty;
		}

		private void ShowStatus(string message, bool isError)
		{
			StatusMessage = message;
			IsStatusError = isError;
			IsStatusVisible = true;
		}

		private void HideStatus()
		{
			IsStatusVisible = false;
			StatusMessage = string.Empty;
			IsStatusError = false;
		}
	}
}