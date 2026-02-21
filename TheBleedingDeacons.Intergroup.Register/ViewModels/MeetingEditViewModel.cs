using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class MeetingEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MeetingEditViewModel>();

    private readonly IMeetingRepository _meetingRepository;

    [ObservableProperty]
    private Meeting? _meeting;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _editGsrName;

    [ObservableProperty]
    private string? _editGsrEmailPersonal;

    [ObservableProperty]
    private string? _editGsrPhone;

    [ObservableProperty]
    private string? _editMeetingGenericEmail;

    [ObservableProperty]
    private bool _editUsingGeneric;

    [ObservableProperty]
    private bool _canConfirm;

    // Display properties that will notify when changed
    [ObservableProperty]
    private string? _displayGsrName;

    [ObservableProperty]
    private string? _displayGsrEmailPersonal;

    [ObservableProperty]
    private string? _displayGsrPhone;

    [ObservableProperty]
    private string? _displayMeetingGenericEmail;

    [ObservableProperty]
    private bool _displayUsingGeneric;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _canSave;

    private readonly IPopupNotification _popupService;
    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;

    public MeetingEditViewModel(IMeetingRepository meetingRepository, IPopupNotification popupService, IAttendanceRegistration<Meeting> attendanceRegistration)
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
            // Load meeting by ID if only ID was passed
            _ = LoadMeetingByIdAsync(meetingId);
        }
    }

    private async Task LoadMeetingByIdAsync(int meetingId)
    {
        try
        {
            var meeting = await _meetingRepository.GetMeetingByIdAsync(meetingId);
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
        DisplayGsrName = Meeting.Group?.Gsr?.Name;
        DisplayGsrEmailPersonal = Meeting.Group?.Gsr?.EmailPersonal;
        DisplayGsrPhone = Meeting.Group?.Gsr?.Phone;
        DisplayMeetingGenericEmail = Meeting.MeetingGenericEmail;
        DisplayUsingGeneric = Meeting.UsingGeneric ?? false;
        Title = Meeting.Name;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
        {
            StartEditing();
        }
        UpdateCanSave();
    }

    partial void OnEditGsrNameChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditGsrEmailPersonalChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditGsrPhoneChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditMeetingGenericEmailChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditUsingGenericChanged(bool value)
    {
        UpdateCanSave();
    }

    

    private void StartEditing()
    {
        if (Meeting == null) return;

        EditGsrName = DisplayGsrName;
        EditGsrEmailPersonal = DisplayGsrEmailPersonal;
        EditGsrPhone = DisplayGsrPhone;
        EditMeetingGenericEmail = DisplayMeetingGenericEmail;
        EditUsingGeneric = DisplayUsingGeneric;

        UpdateCanSave();
    }

    private void UpdateCanConfirm()
    {
        // GSR Name is required
        bool hasGsrName = !string.IsNullOrWhiteSpace(DisplayGsrName);

        // Either GSR Phone or GSR Email is required (but not necessarily both)
        bool hasGsrContact = !string.IsNullOrWhiteSpace(DisplayGsrPhone) ||
                            !string.IsNullOrWhiteSpace(DisplayGsrEmailPersonal);

        CanConfirm = hasGsrName && hasGsrContact;

        // Update validation message
        UpdateValidationMessage(hasGsrName, hasGsrContact);
    }

    private void UpdateValidationMessage(bool hasGsrName, bool hasGsrContact)
    {
        var errors = new List<string>();

        if (!hasGsrName)
            errors.Add("GSR Name is required");

        if (!hasGsrContact)
            errors.Add("Either GSR Email or GSR Phone is required");

        HasValidationErrors = errors.Count > 0;
        ValidationMessage = errors.Count > 0 ? string.Join(", ", errors) : null;
    }

    private void UpdateCanSave()
    {
        if (!IsEditing)
        {
            CanSave = false;
            return;
        }

        bool hasGsrName = !string.IsNullOrWhiteSpace(EditGsrName);
        bool hasGsrContact = !string.IsNullOrWhiteSpace(EditGsrPhone) ||
                            !string.IsNullOrWhiteSpace(EditGsrEmailPersonal);

        CanSave = hasGsrName && hasGsrContact;
    }

    [RelayCommand]
    private void StartEdit()
    {
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Meeting == null) return;

        await ShowSaveFeedback();

        // Validate required fields before saving
        bool hasGsrName = !string.IsNullOrWhiteSpace(EditGsrName);
        bool hasGsrContact = !string.IsNullOrWhiteSpace(EditGsrPhone) ||
                            !string.IsNullOrWhiteSpace(EditGsrEmailPersonal);

        if (!hasGsrName || !hasGsrContact)
        {
            var errors = new List<string>();

            if (!hasGsrName)
                errors.Add("GSR Name is required");

            if (!hasGsrContact)
                errors.Add("Either GSR Email or GSR Phone is required");

            string errorMessage = string.Join("\n", errors);
            await Shell.Current.DisplayAlert("Validation Error", $"Please fix the following errors:\n\n{errorMessage}", "OK");
            return;
        }

        try
        {
            // Copy edited values back to the meeting
            // Persist edited GSR values back to Group.Gsr
            if (Meeting.Group != null)
            {
                Meeting.Group.Gsr ??= new Models.Member { GroupId = Meeting.Group.ID };
                Meeting.Group.Gsr.Name = EditGsrName;
                Meeting.Group.Gsr.EmailPersonal = EditGsrEmailPersonal;
                Meeting.Group.Gsr.Phone = EditGsrPhone;
            }
            Meeting.MeetingGenericEmail = EditMeetingGenericEmail;
            Meeting.UsingGeneric = EditUsingGeneric;

            // Save to repository
            var savedMeeting = await _meetingRepository.SaveMeetingAsync(Meeting);

            // Update the Meeting property to trigger UI refresh
            Meeting = savedMeeting;

            // Exit editing mode
            IsEditing = false;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to save meeting: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (IsEditing)
        {
            // Check if there are unsaved changes
            bool hasChanges = HasUnsavedChanges();

            if (hasChanges)
            {
                bool shouldDiscard = await Shell.Current.DisplayAlert(
                    "Discard Changes?",
                    "You have unsaved changes. Are you sure you want to discard them?",
                    "Discard",
                    "Keep Editing");

                if (!shouldDiscard)
                    return;
            }

            IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!CanConfirm) return;

        try
        {
            // Mark as attended and save
            if (Meeting != null)
            {
                await _attendanceRegistration.Register(Meeting);

                string personalName = Meeting.GetFirstName();

                // Show success popup
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

    private bool HasUnsavedChanges()
    {
        return EditGsrName != DisplayGsrName ||
               EditGsrEmailPersonal != DisplayGsrEmailPersonal ||
               EditGsrPhone != DisplayGsrPhone ||
               EditMeetingGenericEmail != DisplayMeetingGenericEmail ||
               EditUsingGeneric != DisplayUsingGeneric;
    }

    private async Task ShowSaveFeedback()
    {
        await Task.Delay(100);
        await Toast.Make("Updating Meeting...", ToastDuration.Short).Show();
    }

}
