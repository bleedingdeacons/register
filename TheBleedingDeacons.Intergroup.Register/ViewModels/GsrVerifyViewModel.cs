using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

[QueryProperty(nameof(MeetingId), "groupId")]
[QueryProperty(nameof(Edited), "edited")]
public partial class GsrVerifyViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<GsrVerifyViewModel>();

    private readonly DataService _dataService;
    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPopupNotification _popupService;

    [ObservableProperty]
    private string attendedStatusText = string.Empty;

    [ObservableProperty]
    private int meetingId;

    // Initialize Meeting to avoid null reference exceptions in bindings
    [ObservableProperty]
    private Meeting meeting = new();

    [ObservableProperty]
    private bool edited;

    [ObservableProperty]
    private bool standingIn = false;

    [ObservableProperty]
    private string? standinEmail;

    [ObservableProperty]
    private string? standinName;

    [ObservableProperty]
    private bool canRegister = false;

    [ObservableProperty]
    private bool isLoading;

    public GsrVerifyViewModel(DataService dataService, IAttendanceRegistration<Meeting> attendanceRegistration, IMeetingRepository meetingRepository, IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _dataService = dataService;
        _meetingRepository = meetingRepository;
        _popupService = popupService;
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("ApplyQueryAttributes called with {Count} parameters", query.Count);

        foreach (var kvp in query)
        {
            Logger.Information("  Query param: {Key} = {Value} (Type: {Type})",
                kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
        }

        // Handle edited flag
        if (query.TryGetValue("edited", out var editedObj) &&
            editedObj?.ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Meeting));
                // Null-safe check for HasAll
                CanRegister = Meeting?.HasAll() ?? false;
            });
        }

        // Handle groupId passed as string from navigation
        if (query.TryGetValue("groupId", out var meetingIdObj))
        {
            int parsedMeetingId = 0;

            if (meetingIdObj is string meetingIdStr)
            {
                Logger.Information("Parsing meetingId from string: {MeetingIdStr}", meetingIdStr);
                int.TryParse(meetingIdStr, out parsedMeetingId);
            }
            else if (meetingIdObj is int intValue)
            {
                Logger.Information("MeetingId is already int: {IntValue}", intValue);
                parsedMeetingId = intValue;
            }

            Logger.Information("Parsed meetingId: {ParsedMeetingId}", parsedMeetingId);

            if (parsedMeetingId > 0)
            {
                MeetingId = parsedMeetingId;
            }
        }
    }

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

        await Shell.Current.GoToAsync(nameof(GsrEditPage), parameters);
    }

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

            // Show success popup
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

    [RelayCommand]
    public async Task Cancel()
    {
        await Shell.Current.GoToAsync("///MainPage");
    }

    private async Task LoadMeetingAsync(int meetingId)
    {
        Logger.Information("LoadMeetingAsync called with meetingId: {MeetingId}", meetingId);

        if (IsLoading) return;

        try
        {
            IsLoading = true;

            var loadedMeeting = await _meetingRepository.GetMeetingDirectlyAsync(meetingId);

            Logger.Information("Meeting loaded: {MeetingName}, GSR: {GsrName}",
                loadedMeeting?.Name ?? "null",
                loadedMeeting?.GsrName ?? "null");

            if (loadedMeeting != null)
            {
                // All UI updates happen here on the main thread
                Meeting = loadedMeeting;

                // Set title with null-safe checks
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

                // Force property change notification for bindings
                OnPropertyChanged(nameof(Meeting));

                Logger.Information("UI updated - Meeting: {MeetingName}, CanRegister: {CanRegister}",
                    Meeting?.Name, CanRegister);
            }
            else
            {
                Logger.Warning("Meeting not found for ID: {MeetingId}", meetingId);
                var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert(
                        "Not Found",
                        $"Meeting with ID {meetingId} was not found.",
                        "OK");
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
                    await mainPage.DisplayAlert(
                        "Error",
                        $"Failed to load meeting: {ex.Message}",
                        "OK");
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
}
