using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
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
    public partial class AdminViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AdminViewModel>();

        private readonly DataService _dataService;
        private readonly IIntergroupMeetingRepository _intergroupMeetingRepository;
        private readonly IConfigurationService _configService;
        private readonly UnityDbContext _dbContext;

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
        public bool IsBusy => Phase is MeetingPhase.Syncing or MeetingPhase.Finishing;

        // ── Status / progress ────────────────────────────────────────

        [ObservableProperty]
        private string statusMessage = string.Empty;

        [ObservableProperty]
        private bool isStatusError = false;

        [ObservableProperty]
        private bool isStatusVisible = false;

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
            UnityDbContext dbContext)
        {
            _dataService = dataService;
            _intergroupMeetingRepository = intergroupMeetingRepository;
            _configService = configService;
            _dbContext = dbContext;

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
                return;
            }

            try
            {
                Phase = MeetingPhase.Syncing;
                ShowStatus("Syncing data from Unity...", false);

                var (meetings, positions, members, groups, contacts, intergroupMeetings) =
                    await _dataService.ImportWithSnapshotAsync();

                ShowStatus(
                    $"Sync complete: {groups} groups, {meetings} meetings, " +
                    $"{members} members, {positions} positions.",
                    false);

                Logger.Information(
                    "Start Meeting sync complete: {Groups} groups, {Meetings} meetings, {Members} members",
                    groups, meetings, members);

                // Load the intergroup meetings list for selection
                await LoadMeetingsAsync();

                Phase = MeetingPhase.SelectMeeting;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Start Meeting sync failed");
                ShowStatus($"Sync failed: {ex.Message}", true);
                Phase = MeetingPhase.NotStarted;
            }
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

            try
            {
                Phase = MeetingPhase.Finishing;
                ShowStatus("Pushing changes to Unity...", false);

                var (meetings, positions, members, groups, contacts, intergroupMeetings,
                     created, modified, registered, errors) =
                    await _dataService.ImportWithReconciliationAsync();

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

                Phase = MeetingPhase.Completed;
                HideStatus();

                Logger.Information(
                    "Meeting finished: {Created} created, {Modified} modified, {Registered} registered, {Errors} errors",
                    created, modified, registered, errors);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Finish Meeting reconciliation failed");
                ShowStatus($"Reconciliation failed: {ex.Message}", true);
                Phase = MeetingPhase.InProgress; // Allow retry
            }
        }

        /// <summary>
        /// Reset back to NotStarted so the user can begin a new session.
        /// </summary>
        [RelayCommand]
        private void NewSession()
        {
            Phase = MeetingPhase.NotStarted;
            FinishSummary = string.Empty;
            Meetings.Clear();
            HideStatus();
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
                        .GetByIdAsync(config.ActiveIntergroupMeetingId.Value);

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
            var meetings = await _intergroupMeetingRepository.GetAllAsync();

            Meetings.Clear();
            foreach (var meeting in meetings)
                Meetings.Add(meeting);

            Logger.Information("Loaded {Count} intergroup meetings", meetings.Count);
        }

        private async Task ResetAllRegisteredStateAsync()
        {
            try
            {
                await _dbContext.Groups
                    .Where(g => g.Registered)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.Registered, false));

                await _dbContext.Positions
                    .Where(p => p.Registered)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Registered, false));

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