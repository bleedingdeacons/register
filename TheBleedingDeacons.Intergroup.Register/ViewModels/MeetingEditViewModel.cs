using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class MeetingEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MeetingEditViewModel>();

    private readonly IMeetingRepository _meetingRepository;

    [ObservableProperty]
    private Meeting? _meeting;

    [ObservableProperty]
    private bool _canConfirm;

    [ObservableProperty]
    private string? _displayGsrName;

    [ObservableProperty]
    private string? _displayGsrEmail;

    [ObservableProperty]
    private string? _displayGsrPhone;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string? _validationMessage;

    private readonly IPopupNotification _popupService;
    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;

    public MeetingEditViewModel(
        IMeetingRepository meetingRepository,
        IPopupNotification popupService,
        IAttendanceRegistration<Meeting> attendanceRegistration)
    {
        _meetingRepository = meetingRepository ?? throw new ArgumentNullException(nameof(meetingRepository));
        _attendanceRegistration = attendanceRegistration ?? throw new ArgumentNullException(nameof(attendanceRegistration));
        _popupService = popupService;
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("meeting", out var meetingObj) && meetingObj is Meeting meeting)
        {
            Initialize(meeting);
        }
        else if (query.TryGetValue("meetingId", out var meetingIdObj) &&
                 meetingIdObj is string meetingIdStr &&
                 int.TryParse(meetingIdStr, out var meetingId))
        {
            LoadMeetingByIdAsync(meetingId).SafeFireAndForget("LoadMeetingById");
        }
    }

    private async Task LoadMeetingByIdAsync(int meetingId)
    {
        try
        {
            var meeting = await _meetingRepository.GetByIdAsync(meetingId);
            if (meeting != null)
            {
                Initialize(meeting);
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", "Meeting not found.", "OK");
                await Shell.Current.GoToAsync("//MainPage");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to load meeting: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("//MainPage");
        }
    }

    public void Initialize(Meeting meeting)
    {
        Meeting = meeting;
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    partial void OnMeetingChanged(Meeting? value)
    {
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    private void UpdateDisplayProperties()
    {
        if (Meeting == null) return;
        var primaryGsr = Meeting.Group?.Members.FirstOrDefault(m => m.IsGsr);
        DisplayGsrName = primaryGsr?.AnonymousName;
        DisplayGsrEmail = primaryGsr?.PersonalEmail;
        DisplayGsrPhone = primaryGsr?.MobileNumber;
        Title = Meeting.Name;
    }

    private void UpdateCanConfirm()
    {
        bool hasGsrName = !string.IsNullOrWhiteSpace(DisplayGsrName);
        bool hasGsrContact = !string.IsNullOrWhiteSpace(DisplayGsrPhone) ||
                            !string.IsNullOrWhiteSpace(DisplayGsrEmail);

        CanConfirm = hasGsrName && hasGsrContact;

        var errors = new List<string>();
        if (!hasGsrName) errors.Add("GSR Name is required");
        if (!hasGsrContact) errors.Add("Either GSR Email or GSR Phone is required");

        HasValidationErrors = errors.Count > 0;
        ValidationMessage = errors.Count > 0 ? string.Join(", ", errors) : null;
    }

    [RelayCommand]
    private async Task Confirm()
    {
        if (!CanConfirm) return;

        try
        {
            if (Meeting != null)
            {
                await _attendanceRegistration.Register(Meeting);

                string personalName = Meeting.GetFirstName();

                await _popupService.ShowCountdownPopupAsync(
                    "Finished",
                    $"Thanks for registering {personalName}.",
                    async () => await Shell.Current.GoToAsync("//MainPage")
                );
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to confirm meeting: {ex.Message}", "OK");
        }
    }
}
