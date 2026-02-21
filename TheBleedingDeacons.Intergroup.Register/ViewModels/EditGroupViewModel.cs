using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
public partial class EditGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

    // Services
    private readonly DataService _dataService;
    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPopupNotification _popupService;

    // Meeting Properties
    [ObservableProperty]
    private Meeting? meeting;

    [ObservableProperty]
    private int meetingId;

    // GSR Edit Properties
    [ObservableProperty]
    private string? gsrName;

    [ObservableProperty]
    private string? gsrPhone;

    [ObservableProperty]
    private string? gsrEmailPersonal;

    // Verification/Registration Properties
    [ObservableProperty]
    private string attendedStatusText = string.Empty;

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

    // UI State Properties
    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private bool isFormValid;

    [ObservableProperty]
    private string saveButtonText = "Save";

    // Mode Control - determines which UI to show
    [ObservableProperty]
    private bool isVerifyMode = true; // Default to verify mode

    // Validation Error Properties
    [ObservableProperty]
    private string? gsrNameError;

    [ObservableProperty]
    private string? gsrPhoneError;

    [ObservableProperty]
    private string? gsrEmailError;

    [ObservableProperty]
    private bool hasGsrNameError;

    [ObservableProperty]
    private bool hasGsrPhoneError;

    [ObservableProperty]
    private bool hasGsrEmailError;

    public EditGroupViewModel(
        DataService dataService,
        IAttendanceRegistration<Meeting> attendanceRegistration,
        IMeetingRepository meetingRepository,
        IPopupNotification popupService)
    {
        _dataService = dataService;
        _attendanceRegistration = attendanceRegistration;
        _meetingRepository = meetingRepository;
        _popupService = popupService;

        // Initialize with default values
        ValidateForm();
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("ApplyQueryAttributes called with {Count} parameters", query.Count);

        foreach (var kvp in query)
        {
            Logger.Information("  Query param: {Key} = {Value} (Type: {Type})",
                kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
        }

        // Determine mode based on parameters
        // If groupId is provided -> Verify mode (read-only display)
        // If meeting object is provided -> Edit mode (editable fields)
        bool hasGroupId = query.ContainsKey("groupId");
        bool hasMeeting = query.ContainsKey("meeting");

        if (hasGroupId)
        {
            IsVerifyMode = true;
            Logger.Information("Mode: VERIFY (groupId provided)");
        }
        else if (hasMeeting)
        {
            IsVerifyMode = false;
            Logger.Information("Mode: EDIT (meeting object provided)");
        }

        // Handle meeting object from Edit flow
        if (query.ContainsKey("meeting") && query["meeting"] is Meeting meeting)
        {
            Meeting = meeting;
        }

        // Handle edited flag from Verify flow
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
            LoadMeetingData();
            UpdateTitle();
        }
    }

    partial void OnGsrNameChanged(string? value)
    {
        ValidateGsrName();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnGsrPhoneChanged(string? value)
    {
        ValidateGsrPhone();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnGsrEmailPersonalChanged(string? value)
    {
        ValidateGsrEmail();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        SaveButtonText = value ? "Saving..." : "Save";

        // Notify that command can execute state might have changed
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        if (value)
        {
            Title = "Edit GSR Information *";
        }
        else
        {
            Title = "Edit GSR Information";
        }
    }

    partial void OnIsFormValidChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Commands - Registration/Verification Flow

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

        // Navigate to the same page but in Edit mode
        await Shell.Current.GoToAsync(nameof(GroupEditPage), parameters);
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

    #endregion

    #region Commands - Edit Flow

    [RelayCommand]
    private async Task Save()
    {
        if (!IsFormValid)
        {
            ValidateGsrName();
            ValidateGsrPhone();
            ValidateGsrEmail();
            await Shell.Current.DisplayAlert("Validation Error", "Please fix the form errors before saving.", "OK");
            return;
        }

        try
        {
            IsLoading = true;

            if (Meeting != null)
            {
                // Ensure the group has a GSR member entity to update
                if (Meeting.Group != null)
                {
                    Meeting.Group.Gsr ??= new Models.Member { GroupId = Meeting.Group.ID };
                    Meeting.Group.Gsr.Name = GsrName?.Trim();
                    Meeting.Group.Gsr.Phone = string.IsNullOrWhiteSpace(GsrPhone) ? string.Empty : GsrPhone.Trim();
                    Meeting.Group.Gsr.EmailPersonal = string.IsNullOrWhiteSpace(GsrEmailPersonal) ? string.Empty : GsrEmailPersonal.Trim();
                }

                await SaveToDatabase(Meeting);

                HasUnsavedChanges = false;
                await Shell.Current.GoToAsync($"..?edited=true");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to save GSR information: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (IsLoading) return;

        if (HasUnsavedChanges)
        {
            bool shouldCancel = await Shell.Current.DisplayAlert(
                "Unsaved Changes",
                "You have unsaved changes. Are you sure you want to cancel?",
                "Yes", "No");

            if (!shouldCancel) return;
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private void TestCommand()
    {
        System.Diagnostics.Debug.WriteLine("=== Test command executed ===");
        Shell.Current.DisplayAlert("Test", "Command system is working!", "OK");
    }

    #endregion

    #region Private Methods - Loading and Initialization

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
                loadedMeeting?.Group?.Gsr?.Name ?? "null");

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

    private void LoadMeetingData()
    {
        if (Meeting == null) return;

        // Temporarily disable change tracking while loading
        var wasTracking = HasUnsavedChanges;

        var gsr = Meeting.Group?.Gsr;
        GsrName = gsr?.Name;
        GsrPhone = gsr?.Phone;
        GsrEmailPersonal = gsr?.EmailPersonal;

        // Reset change tracking
        HasUnsavedChanges = false;
    }

    private void UpdateTitle()
    {
        if (Meeting != null && !string.IsNullOrEmpty(Meeting.Name))
        {
            Title = $"{Meeting.Name}";
        }
        else
        {
            Title = "Group Service Representative";
        }
    }

    private async Task SaveToDatabase(Meeting meeting)
    {
        await _meetingRepository.SaveMeetingAsync(meeting);
    }

    #endregion

    #region Validation Methods

    private void ValidateGsrName()
    {
        ClearGsrNameError();

        if (string.IsNullOrWhiteSpace(GsrName))
        {
            SetGsrNameError("Your Name is required.");
        }
        else if (GsrName.Trim().Length > 255)
        {
            SetGsrNameError("Your Name cannot exceed 255 characters.");
        }
    }

    private void ValidateGsrPhone()
    {
        ClearGsrPhoneError();

        if (!string.IsNullOrWhiteSpace(GsrPhone))
        {
            if (GsrPhone.Trim().Length > 20)
            {
                SetGsrPhoneError("Phone number cannot exceed 20 characters.");
            }
            // Add more phone validation here if needed
            else if (!IsValidPhoneFormat(GsrPhone.Trim()))
            {
                SetGsrPhoneError("Please check the phone number is valid.");
            }
        }
    }

    private void ValidateGsrEmail()
    {
        ClearGsrEmailError();

        if (!string.IsNullOrWhiteSpace(GsrEmailPersonal))
        {
            if (GsrEmailPersonal.Trim().Length > 255)
            {
                SetGsrEmailError("Email address cannot exceed 255 characters.");
            }
            else if (!IsValidEmail(GsrEmailPersonal.Trim()))
            {
                SetGsrEmailError("Please check the email address is correct.");
            }
        }
    }

    private void ValidateForm()
    {
        IsFormValid = !HasGsrNameError &&
                     !HasGsrPhoneError &&
                     !HasGsrEmailError &&
                     !string.IsNullOrWhiteSpace(GsrName) &&
                     !string.IsNullOrWhiteSpace(GsrPhone) &&
                     !string.IsNullOrWhiteSpace(GsrEmailPersonal);
    }

    private void CheckForUnsavedChanges()
    {
        if (Meeting == null)
        {
            HasUnsavedChanges = false;
            return;
        }

        var gsr = Meeting.Group?.Gsr;
        HasUnsavedChanges = gsr?.Name != GsrName?.Trim() ||
                           gsr?.Phone != GsrPhone?.Trim() ||
                           gsr?.EmailPersonal != GsrEmailPersonal?.Trim();
    }

    private void SetGsrNameError(string error) { GsrNameError = error; HasGsrNameError = true; }
    private void ClearGsrNameError() { GsrNameError = null; HasGsrNameError = false; }
    private void SetGsrPhoneError(string error) { GsrPhoneError = error; HasGsrPhoneError = true; }
    private void ClearGsrPhoneError() { GsrPhoneError = null; HasGsrPhoneError = false; }
    private void SetGsrEmailError(string error) { GsrEmailError = error; HasGsrEmailError = true; }
    private void ClearGsrEmailError() { GsrEmailError = null; HasGsrEmailError = false; }

    private void ClearAllErrors()
    {
        ClearGsrNameError();
        ClearGsrPhoneError();
        ClearGsrEmailError();
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var emailAttribute = new EmailAddressAttribute();
            return emailAttribute.IsValid(email);
        }
        catch { return false; }
    }

    private bool IsValidPhoneFormat(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());
        return digitsOnly.Length >= 7 && digitsOnly.Length <= 15;
    }

    #endregion
}