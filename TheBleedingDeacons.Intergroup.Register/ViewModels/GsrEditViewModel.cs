using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using System.ComponentModel.DataAnnotations;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class GsrEditViewModel : ObservableObject, IQueryAttributable
{
    private static readonly ILogger Logger = AppLogger.ForContext<MeetingSelectionViewModel>();

    private readonly IMeetingRepository _meetingRepository;

    [ObservableProperty]
    private Meeting? meeting;

    [ObservableProperty]
    private string? gsrName;

    [ObservableProperty]
    private string? gsrPhone;

    [ObservableProperty]
    private string? gsrEmailPersonal;

    [ObservableProperty]
    private bool isLoading;

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

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private bool isFormValid;

    [ObservableProperty]
    private string saveButtonText = "Save";


    public GsrEditViewModel(IMeetingRepository meetingRepository)
    {
        _meetingRepository = meetingRepository;

        // Initialize with default values
        ValidateForm();
    }

    // This method is automatically called when the Meeting property changes
    partial void OnMeetingChanged(Meeting? value)
    {
        if (value != null)
        {
            LoadMeetingData();
            UpdateTitle();
        }
    }

    // Property change handlers for real-time validation and UI updates
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

    private void LoadMeetingData()
    {
        if (Meeting == null) return;

        // Temporarily disable change tracking while loading
        var wasTracking = HasUnsavedChanges;

        GsrName = Meeting.GsrName;
        GsrPhone = Meeting.GsrPhone;
        GsrEmailPersonal = Meeting.GsrEmailPersonal;

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
                     !string.IsNullOrWhiteSpace(GsrPhone) && !string.IsNullOrWhiteSpace(GsrEmailPersonal);
    }

    private void CheckForUnsavedChanges()
    {
        if (Meeting == null)
        {
            HasUnsavedChanges = false;
            return;
        }

        HasUnsavedChanges = Meeting.GsrName != GsrName?.Trim() ||
                           Meeting.GsrPhone != GsrPhone?.Trim() ||
                           Meeting.GsrEmailPersonal != GsrEmailPersonal?.Trim();
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
                Meeting.GsrName = GsrName?.Trim();
                Meeting.GsrPhone = string.IsNullOrWhiteSpace(GsrPhone) ? string.Empty : GsrPhone.Trim();
                Meeting.GsrEmailPersonal = string.IsNullOrWhiteSpace(GsrEmailPersonal) ? string.Empty : GsrEmailPersonal.Trim();

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

    // Handle navigation parameters
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.ContainsKey("meeting") && query["meeting"] is Meeting meeting)
        {
            Meeting = meeting;
        }
    }

    private async Task SaveToDatabase(Meeting meeting)
    {
        await _meetingRepository.SaveMeetingAsync(meeting);
    }
}
