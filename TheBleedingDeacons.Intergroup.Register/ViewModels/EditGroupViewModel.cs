using System.Collections.ObjectModel;
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
/// Now member-centric: displays a list of ALL GSRs for the group, allows
/// editing each one, adding new members, and marking members for deletion
/// (with replacement).
///
/// Separated from the verify flow (see <see cref="VerifyGroupViewModel"/>)
/// so each ViewModel handles a single responsibility (ARCH-002).
/// </summary>
public partial class EditGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

    private readonly RegisterContext _context;
    private readonly IPopupNotification _popupService;

    // The group whose GSRs are being managed
    private Group? _group;

    /// <summary>
    /// The currently selected member being edited (null when not editing).
    /// </summary>
    [ObservableProperty]
    private Member? selectedMember;

    /// <summary>
    /// Observable list of active (non-deleted) members for the group.
    /// </summary>
    public ObservableCollection<Member> ActiveMembers { get; } = new();

    /// <summary>
    /// Members that have been marked for deletion during this session.
    /// </summary>
    public ObservableCollection<Member> DeletedMembers { get; } = new();

    // ── Editing fields (bound to the form when a member is selected) ──

    [ObservableProperty]
    private string? editName;

    [ObservableProperty]
    private string? editPhone;

    [ObservableProperty]
    private string? editEmail;

    // ── UI State ──

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private bool isCreatingNew;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private bool isFormValid;

    [ObservableProperty]
    private string saveButtonText = "Save";

    [ObservableProperty]
    private bool hasActiveMembers;

    [ObservableProperty]
    private bool hasDeletedMembers;

    [ObservableProperty]
    private string memberCountText = string.Empty;

    // ── Validation Errors ──

    [ObservableProperty]
    private string? nameError;

    [ObservableProperty]
    private string? phoneError;

    [ObservableProperty]
    private string? emailError;

    [ObservableProperty]
    private bool hasNameError;

    [ObservableProperty]
    private bool hasPhoneError;

    [ObservableProperty]
    private bool hasEmailError;

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
            Logger.Information("Edit mode: group {GroupName} with {GsrCount} GSRs",
                group.Name, group.Gsrs.Count);

            PopulateMemberLists();
            UpdateTitle();
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnEditNameChanged(string? value)
    {
        ValidateName();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnEditPhoneChanged(string? value)
    {
        ValidatePhone();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnEditEmailChanged(string? value)
    {
        ValidateEmail();
        CheckForUnsavedChanges();
        ValidateForm();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        SaveButtonText = value ? "Saving..." : "Save";
        SaveMemberCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditing));
    }

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        UpdateTitle();
    }

    partial void OnIsFormValidChanged(bool value)
    {
        SaveMemberCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Commands

    /// <summary>
    /// Select a member from the list to begin editing their details.
    /// </summary>
    [RelayCommand]
    private void SelectMember(Member member)
    {
        if (member == null) return;

        SelectedMember = member;
        IsCreatingNew = false;
        IsEditing = true;

        EditName = member.Name;
        EditPhone = member.Phone;
        EditEmail = member.EmailPersonal;

        HasUnsavedChanges = false;
        ClearAllErrors();
        ValidateForm();

        Logger.Information("Selected member {MemberName} (ID={MemberId}) for editing", member.Name, member.ID);
    }

    /// <summary>
    /// Begin creating a new member for this group.
    /// </summary>
    [RelayCommand]
    private void AddNewMember()
    {
        SelectedMember = null;
        IsCreatingNew = true;
        IsEditing = true;

        EditName = string.Empty;
        EditPhone = string.Empty;
        EditEmail = string.Empty;

        HasUnsavedChanges = false;
        ClearAllErrors();
        ValidateForm();

        Logger.Information("Starting new member creation for group {GroupName}", _group?.Name);
    }

    /// <summary>
    /// Save the currently-edited or newly-created member.
    /// </summary>
    [RelayCommand]
    private async Task SaveMember()
    {
        if (!IsFormValid)
        {
            ValidateName();
            ValidatePhone();
            ValidateEmail();
            await Shell.Current.DisplayAlert("Validation Error", "Please fix the form errors before saving.", "OK");
            return;
        }

        if (_group == null)
        {
            Logger.Warning("SaveMember called but _group is null");
            return;
        }

        try
        {
            IsLoading = true;

            if (IsCreatingNew)
            {
                // Create a brand-new member for this group
                var newMember = new Member
                {
                    GroupId = _group.ID,
                    Name = EditName?.Trim(),
                    Phone = EditPhone?.Trim() ?? string.Empty,
                    EmailPersonal = EditEmail?.Trim() ?? string.Empty,
                };
                _context.Members.Add(newMember);
                await _context.SaveChangesAsync();

                // Add to the group's in-memory collection and our observable list
                _group.Gsrs.Add(newMember);
                ActiveMembers.Add(newMember);

                Logger.Information("Created new member {MemberName} for group {GroupName}",
                    newMember.Name, _group.Name);
            }
            else if (SelectedMember != null && SelectedMember.ID > 0)
            {
                // Update an existing tracked member
                var tracked = await _context.Members.FindAsync(SelectedMember.ID);
                if (tracked != null)
                {
                    tracked.Name = EditName?.Trim();
                    tracked.Phone = EditPhone?.Trim() ?? string.Empty;
                    tracked.EmailPersonal = EditEmail?.Trim() ?? string.Empty;

                    await _context.SaveChangesAsync();

                    // Reflect changes in the in-memory object for the list
                    SelectedMember.Name = tracked.Name;
                    SelectedMember.Phone = tracked.Phone;
                    SelectedMember.EmailPersonal = tracked.EmailPersonal;

                    Logger.Information("Updated member {MemberName} (ID={MemberId})",
                        tracked.Name, tracked.ID);
                }
            }

            HasUnsavedChanges = false;
            IsEditing = false;
            IsCreatingNew = false;
            SelectedMember = null;

            RefreshMemberCountText();
            UpdateHasActiveMembers();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save member");
            await Shell.Current.DisplayAlert("Error", $"Failed to save member: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Mark the selected member for deletion. If they are being replaced, the user
    /// should add a new member first. The member stays in the DB but is flagged.
    /// </summary>
    [RelayCommand]
    private async Task MarkForDeletion(Member member)
    {
        if (member == null) return;

        bool confirmed = await Shell.Current.DisplayAlert(
            "Mark for Deletion",
            $"Mark \"{member.Name}\" for deletion?\n\nThis member will be hidden and removed on the next sync. You can add a replacement member afterwards.",
            "Mark for Deletion", "Cancel");

        if (!confirmed) return;

        try
        {
            var tracked = await _context.Members.FindAsync(member.ID);
            if (tracked != null)
            {
                tracked.IsMarkedForDeletion = true;
                tracked.MarkedForDeletionDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Also update the in-memory object
                member.IsMarkedForDeletion = true;
                member.MarkedForDeletionDate = tracked.MarkedForDeletionDate;
            }

            ActiveMembers.Remove(member);
            DeletedMembers.Add(member);

            // If we were editing this member, close the form
            if (SelectedMember?.ID == member.ID)
            {
                IsEditing = false;
                IsCreatingNew = false;
                SelectedMember = null;
            }

            RefreshMemberCountText();
            UpdateHasActiveMembers();
            HasDeletedMembers = DeletedMembers.Count > 0;

            Logger.Information("Marked member {MemberName} (ID={MemberId}) for deletion", member.Name, member.ID);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to mark member {MemberId} for deletion", member.ID);
            await Shell.Current.DisplayAlert("Error", $"Failed to mark member for deletion: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Restore a previously-deleted member.
    /// </summary>
    [RelayCommand]
    private async Task RestoreMember(Member member)
    {
        if (member == null) return;

        try
        {
            var tracked = await _context.Members.FindAsync(member.ID);
            if (tracked != null)
            {
                tracked.IsMarkedForDeletion = false;
                tracked.MarkedForDeletionDate = null;
                await _context.SaveChangesAsync();

                member.IsMarkedForDeletion = false;
                member.MarkedForDeletionDate = null;
            }

            DeletedMembers.Remove(member);
            ActiveMembers.Add(member);

            RefreshMemberCountText();
            UpdateHasActiveMembers();
            HasDeletedMembers = DeletedMembers.Count > 0;

            Logger.Information("Restored member {MemberName} (ID={MemberId})", member.Name, member.ID);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to restore member {MemberId}", member.ID);
            await Shell.Current.DisplayAlert("Error", $"Failed to restore member: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Cancel the current edit without saving.
    /// </summary>
    [RelayCommand]
    private async Task CancelEdit()
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

        IsEditing = false;
        IsCreatingNew = false;
        SelectedMember = null;
        ClearAllErrors();
    }

    /// <summary>
    /// Done editing — navigate back to verify page.
    /// </summary>
    [RelayCommand]
    private async Task Done()
    {
        if (IsEditing && HasUnsavedChanges)
        {
            bool shouldLeave = await Shell.Current.DisplayAlert(
                "Unsaved Changes",
                "You have unsaved changes on the current member. Discard and go back?",
                "Discard", "Keep Editing");

            if (!shouldLeave) return;
        }

        await Shell.Current.GoToAsync($"..?edited=true");
    }

    #endregion

    #region Private Methods

    private void PopulateMemberLists()
    {
        ActiveMembers.Clear();
        DeletedMembers.Clear();

        if (_group?.Gsrs != null)
        {
            foreach (var gsr in _group.Gsrs)
            {
                if (gsr.IsMarkedForDeletion)
                    DeletedMembers.Add(gsr);
                else
                    ActiveMembers.Add(gsr);
            }
        }

        UpdateHasActiveMembers();
        HasDeletedMembers = DeletedMembers.Count > 0;
        RefreshMemberCountText();
    }

    private void UpdateHasActiveMembers()
    {
        HasActiveMembers = ActiveMembers.Count > 0;
    }

    private void RefreshMemberCountText()
    {
        var count = ActiveMembers.Count;
        MemberCountText = count switch
        {
            0 => "No members — tap + to add one",
            1 => "1 member",
            _ => $"{count} members"
        };
    }

    private void UpdateTitle()
    {
        var baseName = !string.IsNullOrEmpty(_group?.Name) ? _group!.Name : "Edit Members";
        Title = HasUnsavedChanges ? $"{baseName} *" : baseName;
    }

    #endregion

    #region Validation Methods

    private void ValidateName()
    {
        ClearNameError();

        if (string.IsNullOrWhiteSpace(EditName))
            SetNameError("Name is required.");
        else if (EditName.Trim().Length > 255)
            SetNameError("Name cannot exceed 255 characters.");
    }

    private void ValidatePhone()
    {
        ClearPhoneError();

        if (!string.IsNullOrWhiteSpace(EditPhone))
        {
            if (EditPhone.Trim().Length > 20)
                SetPhoneError("Phone number cannot exceed 20 characters.");
            else if (!IsValidPhoneFormat(EditPhone.Trim()))
                SetPhoneError("Please check the phone number is valid.");
        }
    }

    private void ValidateEmail()
    {
        ClearEmailError();

        if (!string.IsNullOrWhiteSpace(EditEmail))
        {
            if (EditEmail.Trim().Length > 255)
                SetEmailError("Email address cannot exceed 255 characters.");
            else if (!IsValidEmail(EditEmail.Trim()))
                SetEmailError("Please check the email address is correct.");
        }
    }

    private void ValidateForm()
    {
        IsFormValid = !HasNameError &&
                     !HasPhoneError &&
                     !HasEmailError &&
                     !string.IsNullOrWhiteSpace(EditName) &&
                     !string.IsNullOrWhiteSpace(EditPhone) &&
                     !string.IsNullOrWhiteSpace(EditEmail);
    }

    private void CheckForUnsavedChanges()
    {
        if (IsCreatingNew)
        {
            HasUnsavedChanges = !string.IsNullOrWhiteSpace(EditName) ||
                                !string.IsNullOrWhiteSpace(EditPhone) ||
                                !string.IsNullOrWhiteSpace(EditEmail);
            return;
        }

        if (SelectedMember == null) { HasUnsavedChanges = false; return; }

        HasUnsavedChanges = SelectedMember.Name != EditName?.Trim() ||
                            SelectedMember.Phone != EditPhone?.Trim() ||
                            SelectedMember.EmailPersonal != EditEmail?.Trim();
    }

    private void ClearAllErrors()
    {
        ClearNameError();
        ClearPhoneError();
        ClearEmailError();
    }

    private void SetNameError(string error) { NameError = error; HasNameError = true; }
    private void ClearNameError() { NameError = null; HasNameError = false; }
    private void SetPhoneError(string error) { PhoneError = error; HasPhoneError = true; }
    private void ClearPhoneError() { PhoneError = null; HasPhoneError = false; }
    private void SetEmailError(string error) { EmailError = error; HasEmailError = true; }
    private void ClearEmailError() { EmailError = null; HasEmailError = false; }

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
