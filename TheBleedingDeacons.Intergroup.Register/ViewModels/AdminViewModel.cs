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
using TheBleedingDeacons.Unity.Intergroup.Services;

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
		[NotifyCanExecuteChangedFor(nameof(SyncAgainCommand))]
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

		// ── Detailed progress (sync / reconciliation) ────────────────
		//
		// These mirror the SyncProgress reports coming out of the
		// service layer. The view binds to them inside the Syncing /
		// Finishing phase blocks so the user sees what step is running
		// and how far through it is, rather than a single
		// indeterminate spinner for the whole pipeline.
		//
		// IsProgressDeterminate flips based on whether the current
		// stage carries a known total, letting the XAML swap between
		// a ProgressBar and an ActivityIndicator without the
		// view-model having to know which control is on screen.

		/// <summary>
		/// Human-readable description of the current sync step
		/// (e.g. "Fetching members (page 2 of 4)"). Empty when no
		/// sync is in flight.
		/// </summary>
		[ObservableProperty]
		private string progressMessage = string.Empty;

		/// <summary>
		/// Items processed so far in the current stage. Combined with
		/// <see cref="ProgressTotal"/> to drive a determinate progress bar.
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ProgressFraction))]
		[NotifyPropertyChangedFor(nameof(ProgressCountLabel))]
		private int progressCurrent;

		/// <summary>
		/// Total items expected in the current stage, or zero when the
		/// stage is indeterminate / not yet known.
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ProgressFraction))]
		[NotifyPropertyChangedFor(nameof(ProgressCountLabel))]
		private int progressTotal;

		/// <summary>
		/// True when the current stage knows its total and the UI
		/// should show a determinate progress bar; false for the
		/// indeterminate spinner fallback.
		/// </summary>
		[ObservableProperty]
		private bool isProgressDeterminate;

		/// <summary>
		/// 0–1 value bound to <c>ProgressBar.Progress</c>. Returns 0
		/// when no total is known to keep the bar at the start instead
		/// of jumping when the first determinate report arrives.
		/// </summary>
		public double ProgressFraction =>
			ProgressTotal > 0
				? Math.Clamp((double)ProgressCurrent / ProgressTotal, 0, 1)
				: 0;

		/// <summary>
		/// Compact "n / N" label shown next to the progress bar, or
		/// empty when there's no total to count against.
		/// </summary>
		public string ProgressCountLabel =>
			ProgressTotal > 0 ? $"{ProgressCurrent} / {ProgressTotal}" : string.Empty;

		// ── Sync error state ─────────────────────────────────────────

		[ObservableProperty]
		private bool hasSyncError = false;

		[ObservableProperty]
		private bool hasSyncedSuccessfully = false;

		[ObservableProperty]
		private bool hasFinishSyncError = false;

		// ── Connectivity ─────────────────────────────────────────────

		/// <summary>
		/// True when the device currently reports an Internet-capable network
		/// connection. Tracked live via <see cref="Connectivity.ConnectivityChanged"/>
		/// so the Start Meeting / Retry Sync buttons re-evaluate their
		/// CanExecute as soon as the user toggles airplane mode, walks out of
		/// Wi-Fi range, etc. Both commands need to hit the Unity API so they
		/// require Internet — not just any network — before they're allowed
		/// to fire.
		/// </summary>
		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(LoadUnityCommand))]
		[NotifyCanExecuteChangedFor(nameof(RetrySyncCommand))]
		[NotifyCanExecuteChangedFor(nameof(SyncAgainCommand))]
		private bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

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
		//
		// Wrap each IntergroupMeeting in a tiny row-view-model so the
		// "is this the selected row?" state lives on a property the
		// XAML can bind directly (no converter, no equality dance).
		// Tapping a row or its checkbox flips IsSelected on that row
		// and clears it on every other row — single-select, modelled
		// as one ObservableObject per visible row.

		public ObservableCollection<IntergroupMeetingRow> Meetings { get; } = new();

		/// <summary>
		/// The row currently ticked, or <c>null</c> when nothing is
		/// selected. Drives <see cref="StartMeetingCommand"/>'s
		/// CanExecute so the Start Meeting button stays disabled
		/// until the user picks one.
		/// </summary>
		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(StartMeetingCommand))]
		private IntergroupMeetingRow? selectedMeetingRow;

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

			// Live connectivity tracking — the static Connectivity event
			// holds a reference to the handler, so it MUST be unsubscribed
			// in Dispose() or this VM will leak.
			Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

			// Restore phase if a meeting is already active (app restarted mid-session)
			RestorePhaseAsync().SafeFireAndForget("RestorePhase");
		}

		private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
		{
			// Connectivity events fire on a background thread on Android —
			// marshal back to UI so the bound CanExecute / IsEnabled updates
			// don't trip cross-thread checks.
			MainThread.BeginInvokeOnMainThread(() =>
			{
				IsOnline = e.NetworkAccess == NetworkAccess.Internet;
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;

				// Detach row PropertyChanged subscriptions so the rows
				// can be GC'd alongside the VM. Without this the rows
				// would outlive the VM via the delegate's implicit
				// strong reference back to its target.
				foreach (var row in Meetings)
					row.PropertyChanged -= OnRowSelectionChanged;
			}
			base.Dispose(disposing);
		}

		// =============================================================
		// Load Unity (sync)
		// =============================================================

		/// <summary>
		/// Step 1: Sync from Unity and capture a snapshot, then show
		/// the meeting selection list. Disabled while offline — the sync
		/// has to reach the Unity API to do anything useful.
		///
		/// <para>
		/// Previously named <c>StartMeeting</c> when this command both
		/// triggered the sync and started the meeting in one step. The
		/// flow has since been split: this command only loads data,
		/// and a separate <see cref="StartMeetingCommand"/> commits
		/// the user's selection.
		/// </para>
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanLoadUnity))]
		private async Task LoadUnity()
		{
			var config = await _configService.LoadUnityConfigurationAsync();
			if (!config.IsValid())
			{
				ShowStatus("Unity API not configured, Go to Settings → Unity API Settings first.", true);
				HasSyncError = true;
				return;
			}

			Logger.Information(
				"LoadUnity config — BaseUrl: {BaseUrl}, ApiKey: {ApiKeyStatus}, ActiveMeetingId: {ActiveMeetingId}",
				config.BaseUrl,
				string.IsNullOrEmpty(config.ApiKey) ? "(not set)" : "***",
				config.ActiveIntergroupMeetingId);

			try
			{
				Phase = MeetingPhase.Syncing;
				HasSyncError = false;
				ShowStatus("Syncing data from Unity...", false);
				ResetProgress();

				// Progress<T> captures the current SynchronizationContext
				// at construction, so reports made on the service's
				// ConfigureAwait(false) continuations still arrive on
				// the UI thread. The handler updates the observable
				// progress fields, which the page's bindings pick up.
				var progress = new Progress<SyncProgress>(OnSyncProgress);

				var (meetings, positions, members, groups, _, _) =
					await _dataService.ImportWithSnapshotAsync(Token, progress);

				ShowStatus(
					$"Sync complete: {groups} groups, {meetings} meetings, " +
					$"{members} members, {positions} positions.",
					false);

				Logger.Information(
					"Load Unity sync complete: {Groups} groups, {Meetings} meetings, {Members} members",
					groups, meetings, members);

				HasSyncedSuccessfully = true;
				ResetProgress();

				// Load the intergroup meetings list for selection
				await LoadMeetingsAsync();

				Phase = MeetingPhase.SelectMeeting;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Load Unity sync failed");
				ShowStatus($"Sync failed: {ex.Message}", true);
				HasSyncError = true;
				ResetProgress();
				Phase = MeetingPhase.NotStarted;
			}
		}

		/// <summary>
		/// Retry the Unity load after a previous failure. Same connectivity
		/// guard as <see cref="LoadUnity"/> — retrying without Internet
		/// would just hit the same network failure again.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanLoadUnity))]
		private async Task RetrySync()
		{
			await LoadUnity();
		}

		/// <summary>
		/// Re-pull from Unity while the user is still on the meeting-
		/// selection screen — useful if a meeting was added in Unity after
		/// the initial sync. Disabled once an intergroup meeting has been
		/// selected (the session is committed at that point) and while
		/// offline. The button itself is only shown during SelectMeeting.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanSyncAgain))]
		private async Task SyncAgain()
		{
			await LoadUnity();
		}

		/// <summary>
		/// Shared CanExecute for <see cref="LoadUnity"/> and
		/// <see cref="RetrySync"/>. Re-evaluated whenever IsOnline
		/// changes via [NotifyCanExecuteChangedFor].
		/// </summary>
		private bool CanLoadUnity() => IsOnline;

		/// <summary>
		/// Sync Again is allowed only while online AND no meeting has yet
		/// been committed to. Once Phase moves into InProgress the session
		/// is locked to the chosen meeting, so re-pulling Unity data would
		/// risk losing in-flight registrations.
		/// </summary>
		private bool CanSyncAgain() => IsOnline && !IsInProgress;

		// =============================================================
		// Select / Start Meeting
		// =============================================================

		/// <summary>
		/// Toggle the checkbox / selected state for one row. Bound to
		/// the surrounding card's TapGestureRecognizer so a tap
		/// anywhere on the row flips its IsSelected — which then
		/// propagates through <see cref="OnRowSelectionChanged"/> to
		/// enforce single-select. Tapping the already-selected row
		/// clears the selection.
		///
		/// <para>
		/// The CheckBox itself is two-way bound to IsSelected, so
		/// tapping the box directly bypasses this command and goes
		/// straight to the property change. Both paths land in the
		/// same OnRowSelectionChanged handler — that's the single
		/// chokepoint where single-select invariants are applied.
        /// </para>
		/// </summary>
		[RelayCommand]
		private void ToggleSelectMeeting(IntergroupMeetingRow? row)
		{
			if (row == null) return;
			row.IsSelected = !row.IsSelected;
		}

		/// <summary>
		/// Property-changed handler subscribed to every row when the
		/// list is loaded. Fires whenever <c>IsSelected</c> changes on
		/// any row, regardless of whether the change came from the
		/// checkbox's two-way binding, the card-tap toggle command,
		/// or programmatic state restoration.
		///
		/// <para>
		/// Centralising single-select enforcement here keeps the rest
		/// of the code dumb: rows just own a bool, the toggle command
		/// just flips it, the checkbox just two-way binds to it. Any
		/// path that produces "this row is now selected" funnels
		/// through this handler to clear the others.
		/// </para>
		/// </summary>
		private void OnRowSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(IntergroupMeetingRow.IsSelected)) return;
			if (sender is not IntergroupMeetingRow changed) return;

			if (changed.IsSelected)
			{
				// Clear every other row that's still ticked. Iterates
				// the whole list rather than only the remembered
				// SelectedMeetingRow so a transient "two rows ticked"
				// state self-heals.
				foreach (var other in Meetings)
				{
					if (other != changed && other.IsSelected)
						other.IsSelected = false;
				}
				SelectedMeetingRow = changed;
			}
			else if (SelectedMeetingRow == changed)
			{
				// The row that was selected has been unticked → no
				// pick. Don't touch others; single-select invariant
				// guarantees they're already unticked.
				SelectedMeetingRow = null;
			}
		}

		/// <summary>
		/// Step 2: Commit the user's pick. Writes the active meeting
		/// to config, resets per-session flags, navigates to the main
		/// page. Disabled until the user has ticked a row.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanStartMeeting))]
		private async Task StartMeeting()
		{
			var row = SelectedMeetingRow;
			if (row == null) return;

			var meeting = row.Meeting;

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

		/// <summary>
		/// Start Meeting can only fire once the user has ticked a row.
		/// Re-evaluated whenever <see cref="SelectedMeetingRow"/>
		/// changes via [NotifyCanExecuteChangedFor].
		/// </summary>
		private bool CanStartMeeting() => SelectedMeetingRow != null;

		// =============================================================
		// Finish Meeting
		// =============================================================

		/// <summary>
		/// Pushes all local changes to Unity, re-syncs, and ends the session.
		/// </summary>
		[RelayCommand]
		private async Task FinishMeeting()
		{
			bool confirmed = await Shell.Current.DisplayAlertAsync(
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
				ResetProgress();

				var progress = new Progress<SyncProgress>(OnSyncProgress);

				var (created, modified, registered, complianceRecorded, errors, warnings) =
					await _dataService.ImportWithReconciliationAsync(Token, progress);

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
					ResetProgress();
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
					$"  • {complianceRecorded} compliance records recorded" +
					(errors > 0 ? $"\n  • {errors} API errors" : "");

				if (warnings > 0)
					Logger.Warning("Meeting finished with {Warnings} API warnings (non-fatal)", warnings);

				Phase = MeetingPhase.Completed;
				HideStatus();
				ResetProgress();

				Logger.Information(
					"Meeting finished: {Created} created, {Modified} modified, {Registered} registered, " +
					"{Compliance} compliance recorded, {Errors} errors, {Warnings} warnings",
					created, modified, registered, complianceRecorded, errors, warnings);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Finish Meeting reconciliation failed");
				ShowStatus($"Reconciliation failed: {ex.Message}", true);
				HasFinishSyncError = true;
				ResetProgress();
				// Remain at Finishing phase so the retry button is visible
			}
		}

		/// <summary>
		/// Purges all data from the local database and resets the session.
		/// Only available once the current meeting has been finished.
		/// </summary>
		[RelayCommand]
		private async Task PurgeDatabase()
		{
			if (Phase != MeetingPhase.Completed)
			{
				Logger.Warning("PurgeDatabase rejected — phase is {Phase}, expected Completed", Phase);
				return;
			}

			bool confirmed = await Shell.Current.DisplayAlertAsync(
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
					var meeting = await _intergroupMeetingRepository
						.GetByIdAsync(config.ActiveIntergroupMeetingId.Value, Token);

					if (meeting != null)
					{
						ActiveMeetingId = config.ActiveIntergroupMeetingId;
						UpdateActiveMeetingDisplay(meeting);
						Phase = MeetingPhase.InProgress;
						Logger.Information("Restored active meeting session: {Id}", meeting.Id);
					}
					else
					{
						// Meeting ID in SecureStorage is stale (DB was cleared) — clean up
						await _configService.SaveActiveIntergroupMeetingAsync(null);
						Logger.Information(
							"Cleared stale active meeting ID {Id} — meeting no longer in database",
							config.ActiveIntergroupMeetingId.Value);
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

			// Detach any previous handlers — re-loads are common
			// (Sync Again, Retry Sync) and leaving stale subscriptions
			// in place would leak rows that the GC can't collect
			// because the AdminViewModel is still holding their
			// PropertyChanged delegates.
			foreach (var oldRow in Meetings)
				oldRow.PropertyChanged -= OnRowSelectionChanged;

			Meetings.Clear();
			SelectedMeetingRow = null;

			foreach (var meeting in meetings)
			{
				var row = new IntergroupMeetingRow(meeting);
				row.PropertyChanged += OnRowSelectionChanged;
				Meetings.Add(row);
			}

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

		/// <summary>
		/// Handler for <see cref="Progress{T}"/> reports coming out of
		/// the sync / reconciliation pipeline. Already marshalled onto
		/// the UI thread by Progress&lt;T&gt; (which captures the
		/// sync-context at construction), so we only have to translate
		/// the report into the bound observable fields.
		/// </summary>
		private void OnSyncProgress(SyncProgress report)
		{
			ProgressMessage = report.Message;

			if (report.Total is int total && total > 0)
			{
				ProgressTotal = total;
				ProgressCurrent = Math.Clamp(report.Current, 0, total);
				IsProgressDeterminate = true;
			}
			else
			{
				// Indeterminate stage — drop the bar back to zero so
				// the next determinate stage starts clean rather than
				// inheriting the previous fraction. The XAML hides
				// the bar entirely when IsProgressDeterminate is false.
				ProgressTotal = 0;
				ProgressCurrent = 0;
				IsProgressDeterminate = false;
			}
		}

		/// <summary>
		/// Clears progress fields so the next sync starts from a known
		/// blank state. Called at the start of a sync, on completion,
		/// and on error — the page should never show stale progress
		/// from a previous run.
		/// </summary>
		private void ResetProgress()
		{
			ProgressMessage = string.Empty;
			ProgressCurrent = 0;
			ProgressTotal = 0;
			IsProgressDeterminate = false;
		}
	}

	/// <summary>
	/// UI-side wrapper around an <see cref="IntergroupMeeting"/> entity
	/// so each visible row can carry its own checkbox state without
	/// polluting the entity model. The view-model owns the row list
	/// and is responsible for keeping at most one row's
	/// <see cref="IsSelected"/> set to <c>true</c> at a time.
	/// </summary>
	public partial class IntergroupMeetingRow : ObservableObject
	{
		public IntergroupMeetingRow(IntergroupMeeting meeting)
		{
			Meeting = meeting;
		}

		/// <summary>The underlying meeting entity. Bound to via <c>Meeting.Title</c> etc.</summary>
		public IntergroupMeeting Meeting { get; }

		/// <summary>
		/// True while this row is the user's current pick. Two-way
		/// bound from a CheckBox in the row template; tapping the
		/// checkbox or the surrounding card both end up setting this
		/// via <see cref="AdminViewModel.ToggleSelectMeeting"/>.
		/// </summary>
		[ObservableProperty]
		private bool isSelected;
	}
}