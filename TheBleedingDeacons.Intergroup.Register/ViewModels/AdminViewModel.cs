using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using Microsoft.EntityFrameworkCore;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class AdminViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AdminViewModel>();

        private readonly RegisterContext _context;
        private readonly IConfigurationService _configService;

        [ObservableProperty]
        private bool isLoading = false;

        [ObservableProperty]
        private bool isDataLoaded = false;

        [ObservableProperty]
        private string? errorMessage = null;

        [ObservableProperty]
        private int? activeMeetingId = null;

        /// <summary>The date string of whichever meeting is currently active, for display.</summary>
        [ObservableProperty]
        private string activeMeetingDate = string.Empty;

        /// <summary>The title of whichever meeting is currently active, for display.</summary>
        [ObservableProperty]
        private string activeMeetingTitle = string.Empty;

        public ObservableCollection<IntergroupMeeting> Meetings { get; } = new();

        public AdminViewModel(RegisterContext context, IConfigurationService configService)
        {
            _context = context;
            _configService = configService;
        }

        [RelayCommand]
        public async Task LoadDataAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                IsDataLoaded = false;
                ErrorMessage = null;

                await Task.Yield();

                var config = await _configService.LoadUnityConfigurationAsync();
                ActiveMeetingId = config.ActiveIntergroupMeetingId;

                var meetings = await _context.IntergroupMeetings
                    .OrderByDescending(m => m.Date)
                    .ToListAsync();

                Logger.Information("Loaded {Count} intergroup meetings from database", meetings.Count);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Meetings.Clear();
                    foreach (var meeting in meetings)
                    {
                        meeting.IsActive = meeting.ID == ActiveMeetingId;
                        Meetings.Add(meeting);
                    }

                    UpdateActiveMeetingDate();
                    IsDataLoaded = true;
                    IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load intergroup meetings");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorMessage = $"Failed to load meetings: {ex.Message}";
                    IsDataLoaded = true;
                    IsLoading = false;
                });
            }
        }

        [RelayCommand]
        public async Task SelectMeetingAsync(IntergroupMeeting meeting)
        {
            if (meeting == null) return;

            await _configService.SaveActiveIntergroupMeetingAsync(meeting.ID);

            // Update IsActive on all meetings
            foreach (var m in Meetings)
                m.IsActive = m.ID == meeting.ID;

            ActiveMeetingId = meeting.ID;
            UpdateActiveMeetingDate();

            Logger.Information("Active intergroup meeting set to ID {Id}, Title {Title}, Date {Date}", meeting.ID, meeting.Title, meeting.Date);

            var label = string.IsNullOrWhiteSpace(meeting.Title) ? meeting.Date : $"{meeting.Title} ({meeting.Date})";
            await Shell.Current.DisplayAlert(
                "Meeting Selected",
                $"Active meeting set to {label}. Attendance will be recorded against this meeting.",
                "OK");
        }

        private void UpdateActiveMeetingDate()
        {
            if (ActiveMeetingId.HasValue)
            {
                var match = Meetings.FirstOrDefault(m => m.ID == ActiveMeetingId.Value);
                ActiveMeetingDate = match?.Date ?? string.Empty;
                ActiveMeetingTitle = match?.Title ?? string.Empty;
            }
            else
            {
                ActiveMeetingDate = string.Empty;
                ActiveMeetingTitle = string.Empty;
            }
        }
    }
}