using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class AdminViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AdminViewModel>();

        private readonly IIntergroupMeetingRepository _intergroupMeetingRepository;
        private readonly IConfigurationService _configService;
        private readonly UnityDbContext _dbContext;

        [ObservableProperty]
        private bool isLoading = false;

        [ObservableProperty]
        private bool isDataLoaded = false;

        [ObservableProperty]
        private string? errorMessage = null;

        [ObservableProperty]
        private int? activeMeetingId = null;

        [ObservableProperty]
        private string activeMeetingDate = string.Empty;

        [ObservableProperty]
        private string activeMeetingTitle = string.Empty;

        public ObservableCollection<IntergroupMeeting> Meetings { get; } = new();

        public AdminViewModel(
            IIntergroupMeetingRepository intergroupMeetingRepository,
            IConfigurationService configService,
            UnityDbContext dbContext)
        {
            _intergroupMeetingRepository = intergroupMeetingRepository;
            _configService = configService;
            _dbContext = dbContext;
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

                var meetings = await _intergroupMeetingRepository.GetAllAsync();

                Logger.Information("Loaded {Count} intergroup meetings from database", meetings.Count);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Meetings.Clear();
                    foreach (var meeting in meetings)
                    {
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

            await _configService.SaveActiveIntergroupMeetingAsync(meeting.Id);

            // Reset all registered flags when switching to a new meeting
            await ResetAllRegisteredStateAsync();

            ActiveMeetingId = meeting.Id;
            UpdateActiveMeetingDate();

            Logger.Information("Active intergroup meeting set to ID {Id}, Title {Title}, Date {Date}",
                meeting.Id, meeting.Title, meeting.Date);

            var label = string.IsNullOrWhiteSpace(meeting.Title) ? meeting.Date : $"{meeting.Title} ({meeting.Date})";
            await Shell.Current.DisplayAlert(
                "Meeting Selected",
                $"Active meeting set to {label}. Attendance will be recorded against this meeting.",
                "OK");
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

        private void UpdateActiveMeetingDate()
        {
            if (ActiveMeetingId.HasValue)
            {
                var match = Meetings.FirstOrDefault(m => m.Id == ActiveMeetingId.Value);
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
