using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the read-only verification and registration flow for a meeting group.
/// The user confirms their details are correct (Yes) or navigates to edit them (No).
/// 
/// Split from EditGroupViewModel (ARCH-002) to separate verify (read-only Yes/No)
/// from edit (editable form) concerns.
/// </summary>
[QueryProperty(nameof(MeetingId), "groupId")]
[QueryProperty(nameof(Edited), "edited")]
public partial class VerifyGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<VerifyGroupViewModel>();

    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPopupNotification _popupService;

    [ObservableProperty]
    private Meeting? meeting;

    [ObservableProperty]
    private int meetingId;

    [ObservableProperty]
    private string attendedStatusText = string.Empty;

    [ObservableProperty]
    private bool edited;

    [ObservableProperty]
    private bool standingIn;

    [ObservableProperty]
    private string? standinEmail;

    [ObservableProperty]
    private string? standinName;

    [ObservableProperty]
    private bool canRegister;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// The first GSR for the selected meeting's group.
    /// Exposed as a flat property so XAML can bind without indexer syntax.
    /// </summary>
    public Member? PrimaryGsr => Meeting?.Group?.Gsrs.FirstOrDefault();

    public VerifyGroupViewModel(
        IAttendanceRegistration<Meeting> attendanceRegistration,
        IMeetingRepository meetingRepository,
        IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _meetingRepository = meetingRepository;
        _popupService = popupService;
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("VerifyGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        // Handle edited flag returning from Edit flow
        if (query.TryGetValue("edited", out var editedObj) &&
            editedObj?.ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Meeting));
                CanRegister = Meeting?.HasAll() ?? false;
            });
        }

        // Handle groupId passed from navigation
        if (query.TryGetValue("groupId", out var meetingIdObj))
        {
            int parsedMeetingId = 0;

            if (meetingIdObj is string meetingIdStr)
                int.TryParse(meetingIdStr, out parsedMeetingId);
            else if (meetingIdObj is int intValue)
                parsedMeetingId = intValue;

            if (parsedMeetingId > 0)
                MeetingId = parsedMeetingId;
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnMeetingIdChanged(int value)
    {
        Logger.Information("OnMeetingIdChanged triggered with value: {Value}", value);

        if (value > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadMeetingAsync(value);
            });
        }
    }

    partial void OnMeetingChanged(Meeting? value)
    {
        if (value != null)
        {
            UpdateTitle();
            OnPropertyChanged(nameof(PrimaryGsr));
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// User indicates their details are NOT correct — navigate to edit page.
    /// </summary>
    [RelayCommand]
    public async Task No()
    {
        if (Meeting == null)
        {
            Logger.Warning("Cannot navigate to edit - Meeting is null");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            ["meeting"] = Meeting
        };

        await Shell.Current.GoToAsync(nameof(GroupEditPage), parameters);
    }

    /// <summary>
    /// User confirms details are correct — register attendance.
    /// </summary>
    [RelayCommand]
    public async Task Yes()
    {
        if (Meeting == null)
        {
            Logger.Warning("Cannot register - Meeting is null");
            return;
        }

        try
        {
            Meeting.ProxyAttendance = StandingIn;
            Meeting.ProxyEmail = StandinEmail;
            Meeting.ProxyName = StandinName;

            await _attendanceRegistration.Register(Meeting);

            string personalName = Meeting.GetFirstName();

            await _popupService.ShowCountdownPopupAsync(
                "Finished",
                $"Thanks for registering {personalName}.",
                async () => await Shell.Current.GoToAsync("//MainPage")
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to register attendance");

            var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (mainPage != null)
            {
                await mainPage.DisplayAlert("Error", $"Failed to register: {ex.Message}", "OK");
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task LoadMeetingAsync(int meetingId)
    {
        Logger.Information("LoadMeetingAsync called with meetingId: {MeetingId}", meetingId);

        if (IsLoading) return;

        try
        {
            IsLoading = true;

            var loadedMeeting = await _meetingRepository.GetMeetingDirectlyAsync(meetingId);

            if (loadedMeeting != null)
            {
                Meeting = loadedMeeting;

                if (!string.IsNullOrEmpty(loadedMeeting.Name) &&
                    !string.IsNullOrEmpty(loadedMeeting.Day) &&
                    !loadedMeeting.Name.Contains(loadedMeeting.Day))
                {
                    Title = $"{loadedMeeting.Name} on {loadedMeeting.Day}";
                }
                else
                {
                    Title = loadedMeeting.Name ?? "Unknown Meeting";
                }

                CanRegister = loadedMeeting.HasAll();
                OnPropertyChanged(nameof(Meeting));
            }
            else
            {
                Logger.Warning("Meeting not found for ID: {MeetingId}", meetingId);
                var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Not Found", $"Meeting with ID {meetingId} was not found.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load meeting {MeetingId}", meetingId);

            try
            {
                var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Error", $"Failed to load meeting: {ex.Message}", "OK");
                }
            }
            catch (Exception alertEx)
            {
                Logger.Error(alertEx, "Failed to show error alert");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateTitle()
    {
        if (Meeting != null && !string.IsNullOrEmpty(Meeting.Name))
        {
            Title = Meeting.Name;
        }
        else
        {
            Title = "Group Service Representative";
        }
    }

    #endregion
}