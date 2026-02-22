using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the editable GSR information form for a meeting group.
/// Receives a <see cref="Meeting"/> object from navigation, allows editing
/// GSR name/phone/email, validates input, and saves back to the database.
///
/// A group can have more than one GSR. Navigation may optionally pass a
/// <c>member</c> to edit an existing GSR, or omit it to create a new one.
///
/// Separated from the verify flow (see <see cref="VerifyGroupViewModel"/>)
/// so each ViewModel handles a single responsibility (ARCH-002).
/// </summary>
public partial class EditGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

    // Services
    private readonly DataService _dataService;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPopupNotification _popupService;

    // Meeting Properties
    [ObservableProperty]
    private Meeting? meeting;

    // The specific GSR being edited (null = creating a new one for the group)
    private Member? _editingMember;

    // GSR Edit Properties
    [ObservableProperty]
    private string? gsrName;

    [ObservableProperty]
    private string? gsrPhone;

    [ObservableProperty]
    private string? gsrEmailPersonal;

    // UI State Properties
    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private bool isFormValid;

    [ObservableProperty]
    private string saveButtonText = "Save";

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
        IMeetingRepository meetingRepository,
        IPopupNotification popupService)
    {
        _dataService = dataService;
        _meetingRepository = meetingRepository;
        _popupService = popupService;

        ValidateForm();
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("EditGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        // Handle meeting object passed from VerifyGroupViewModel
        if (query.ContainsKey("meeting") && query["meeting"] is Meeting meeting)
        {
            Meeting = meeting;
            Logger.Information("Edit mode: received Meeting {MeetingName}", meeting.Name);
        }

        // Optionally receive the specific GSR member to edit.
        // If not supplied a new Member will be created for the group on save.
        if (query.ContainsKey("member") && query["member"] is Member member)
        {
            _editingMember = member;
            Logger.Information("Edit mode: editing existing GSR member ID {MemberId}", member.ID);
        }
        else
        {
            _editingMember = null;
        }
    }

    #endregion

    #region Property Change Handlers

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
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        Title = value ? "Edit GSR Information *" : "Edit GSR Information";
    }

    partial void OnIsFormValidChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Commands

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

            if (Meeting?.Group != null)
            {
                if (_editingMember != null)
                {
                    // Update the specific GSR member being edited
                    _editingMember.Name = GsrName?.Trim();
                    _editingMember.Phone = string.IsNullOrWhiteSpace(GsrPhone) ? string.Empty : GsrPhone.Trim();
                    _editingMember.EmailPersonal = string.IsNullOrWhiteSpace(GsrEmailPersonal) ? string.Empty : GsrEmailPersonal.Trim();
                }
                else
                {
                    // No existing GSR selected — add a new one to the group
                    var newGsr = new Member
                    {
                        GroupId = Meeting.Group.ID,
                        Name = GsrName?.Trim(),
                        Phone = string.IsNullOrWhiteSpace(GsrPhone) ? string.Empty : GsrPhone.Trim(),
                        EmailPersonal = string.IsNullOrWhiteSpace(GsrEmailPersonal) ? string.Empty : GsrEmailPersonal.Trim(),
                    };
                    Meeting.Group.Gsrs.Add(newGsr);
                    _editingMember = newGsr;
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

    #endregion

    #region Private Methods

    private void LoadMeetingData()
    {
        if (Meeting == null) return;

        // Populate fields from the member being edited, or leave blank for a new GSR
        GsrName = _editingMember?.Name;
        GsrPhone = _editingMember?.Phone;
        GsrEmailPersonal = _editingMember?.EmailPersonal;

        HasUnsavedChanges = false;
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

        HasUnsavedChanges = _editingMember?.Name != GsrName?.Trim() ||
                           _editingMember?.Phone != GsrPhone?.Trim() ||
                           _editingMember?.EmailPersonal != GsrEmailPersonal?.Trim();
    }

    private void SetGsrNameError(string error) { GsrNameError = error; HasGsrNameError = true; }
    private void ClearGsrNameError() { GsrNameError = null; HasGsrNameError = false; }
    private void SetGsrPhoneError(string error) { GsrPhoneError = error; HasGsrPhoneError = true; }
    private void ClearGsrPhoneError() { GsrPhoneError = null; HasGsrPhoneError = false; }
    private void SetGsrEmailError(string error) { GsrEmailError = error; HasGsrEmailError = true; }
    private void ClearGsrEmailError() { GsrEmailError = null; HasGsrEmailError = false; }

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