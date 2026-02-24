using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the editable GSR information form for a group.
/// Receives a <see cref="Group"/> object from navigation (extracted from the
/// selected meeting's Group nav property), allows editing the primary GSR's
/// name / phone / email, and saves directly to the Members table.
///
/// Separated from the verify flow (see <see cref="VerifyGroupViewModel"/>)
/// so each ViewModel handles a single responsibility (ARCH-002).
/// </summary>
public partial class EditGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

    private readonly RegisterContext _context;
    private readonly IPopupNotification _popupService;

    // The group whose primary GSR is being edited
    private Group? _group;

    // The specific GSR member being edited (null = creating a new one)
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
        RegisterContext context,
        IPopupNotification popupService)
    {
        _context = context;
        _popupService = popupService;

        ValidateForm();
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("EditGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        if (query.TryGetValue("group", out var groupObj) && groupObj is Group group)
        {
            _group = group;
            _editingMember = group.Gsrs.FirstOrDefault();

            Logger.Information("Edit mode: group {GroupName}, GSR member ID {MemberId}",
                group.Name, _editingMember?.ID);

            PopulateFields();
            UpdateTitle();
        }
    }

    #endregion

    #region Property Change Handlers

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

        if (_group == null)
        {
            Logger.Warning("Save called but _group is null");
            return;
        }

        try
        {
            IsLoading = true;

            if (_editingMember != null && _editingMember.ID > 0)
            {
                // Update the existing tracked member directly
                var tracked = await _context.Members.FindAsync(_editingMember.ID);
                if (tracked != null)
                {
                    tracked.Name = GsrName?.Trim();
                    tracked.Phone = GsrPhone?.Trim() ?? string.Empty;
                    tracked.EmailPersonal = GsrEmailPersonal?.Trim() ?? string.Empty;
                }
            }
            else
            {
                // No GSR on record — insert a new one for this group
                var newGsr = new Member
                {
                    GroupId = _group.ID,
                    Name = GsrName?.Trim(),
                    Phone = GsrPhone?.Trim() ?? string.Empty,
                    EmailPersonal = GsrEmailPersonal?.Trim() ?? string.Empty,
                };
                _context.Members.Add(newGsr);
                _editingMember = newGsr;
            }

            await _context.SaveChangesAsync();

            HasUnsavedChanges = false;
            await Shell.Current.GoToAsync($"..?edited=true");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save GSR information");
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

    private void PopulateFields()
    {
        GsrName = _editingMember?.Name;
        GsrPhone = _editingMember?.Phone;
        GsrEmailPersonal = _editingMember?.EmailPersonal;
        HasUnsavedChanges = false;
    }

    private void UpdateTitle()
    {
        Title = !string.IsNullOrEmpty(_group?.Name) ? _group!.Name : "Edit GSR Information";
    }

    #endregion

    #region Validation Methods

    private void ValidateGsrName()
    {
        ClearGsrNameError();

        if (string.IsNullOrWhiteSpace(GsrName))
            SetGsrNameError("Your Name is required.");
        else if (GsrName.Trim().Length > 255)
            SetGsrNameError("Your Name cannot exceed 255 characters.");
    }

    private void ValidateGsrPhone()
    {
        ClearGsrPhoneError();

        if (!string.IsNullOrWhiteSpace(GsrPhone))
        {
            if (GsrPhone.Trim().Length > 20)
                SetGsrPhoneError("Phone number cannot exceed 20 characters.");
            else if (!IsValidPhoneFormat(GsrPhone.Trim()))
                SetGsrPhoneError("Please check the phone number is valid.");
        }
    }

    private void ValidateGsrEmail()
    {
        ClearGsrEmailError();

        if (!string.IsNullOrWhiteSpace(GsrEmailPersonal))
        {
            if (GsrEmailPersonal.Trim().Length > 255)
                SetGsrEmailError("Email address cannot exceed 255 characters.");
            else if (!IsValidEmail(GsrEmailPersonal.Trim()))
                SetGsrEmailError("Please check the email address is correct.");
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
        if (_group == null) { HasUnsavedChanges = false; return; }

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

    private static bool IsValidEmail(string email)
    {
        try { return new EmailAddressAttribute().IsValid(email); }
        catch { return false; }
    }

    private static bool IsValidPhoneFormat(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 7 && digits.Length <= 15;
    }

    #endregion
}