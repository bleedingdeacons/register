using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Models;
using Group = TheBleedingDeacons.Unity.Intergroup.Entities.Group;
using Member = TheBleedingDeacons.Unity.Intergroup.Entities.Member;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the editable GSR information form for a group.
/// Member-centric: displays a list of ALL GSRs for the group, allows
/// editing each one and adding new members.
///
/// Writes go to the local <see cref="UnityDbContext"/> and will persist until
/// the next Unity sync replaces the data.
///
/// Separated from the verify flow (see <see cref="VerifyGroupViewModel"/>)
/// so each ViewModel handles a single responsibility.
/// </summary>
public partial class EditGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

    private readonly UnityDbContext _context;
    private readonly IPopupNotification _popupService;
    private readonly QueueingUnityApiService _apiService;

    // The group whose GSRs are being managed
    private Group? _group;

    /// <summary>
    /// The currently selected member being edited (null when not editing).
    /// </summary>
    [ObservableProperty]
    private Member? selectedMember;

    /// <summary>
    /// Observable list of active members for the group.
    /// </summary>
    public ObservableCollection<Member> ActiveMembers { get; } = new();

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
        UnityDbContext context,
        IPopupNotification popupService,
        QueueingUnityApiService apiService)
    {
        _context = context;
        _popupService = popupService;
        _apiService = apiService;

        ValidateForm();
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("EditGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        if (query.TryGetValue("group", out var groupObj) && groupObj is Group group)
        {
            _group = group;
            Logger.Information("Edit mode: group {GroupName} with {MemberCount} members",
                group.Name, group.Members.Count);

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

        EditName = member.AnonymousName;
        EditPhone = member.MobileNumber;
        EditEmail = member.PersonalEmail;

        HasUnsavedChanges = false;
        ClearAllErrors();
        ValidateForm();

        Logger.Information("Selected member {MemberName} (ID={MemberId}) for editing",
            member.AnonymousName, member.Id);
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
                // Create a brand-new GSR member for this group
                var newMember = new Member
                {
                    HomeGroupId = _group.Id,
                    AnonymousName = EditName?.Trim() ?? string.Empty,
                    MobileNumber = EditPhone?.Trim(),
                    PersonalEmail = EditEmail?.Trim(),
                    IsGsr = true,
                };
                _context.Members.Add(newMember);
                await _context.SaveChangesAsync();

                // Add to the group's in-memory collection and our observable list
                _group.Members.Add(newMember);
                ActiveMembers.Add(newMember);

                // Push the new member to the Unity API (queued if offline)
                var createRequest = new CreateMemberRequest
                {
                    AnonymousName = newMember.AnonymousName,
                    PersonalEmail = newMember.PersonalEmail,
                    MobileNumber = newMember.MobileNumber,
                    HomeGroupId = newMember.HomeGroupId,
                    IsGsr = true,
                };
                _ = _apiService.CreateMemberAsync(createRequest)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Logger.Error(t.Exception, "Failed to push new member {MemberName} to API", newMember.AnonymousName);
                        else if (t.Result.Success)
                            Logger.Information("Successfully pushed new member {MemberName} to API", newMember.AnonymousName);
                        else
                            Logger.Warning("CreateMember API returned {Code}: {Message}",
                                t.Result.Error?.Code, t.Result.Error?.Message);
                    }, TaskScheduler.Default);

                Logger.Information("Created new member {MemberName} for group {GroupName}",
                    newMember.AnonymousName, _group.Name);
            }
            else if (SelectedMember != null && SelectedMember.Id > 0)
            {
                // Update an existing tracked member
                var tracked = await _context.Members.FindAsync(SelectedMember.Id);
                if (tracked != null)
                {
                    tracked.AnonymousName = EditName?.Trim() ?? string.Empty;
                    tracked.MobileNumber = EditPhone?.Trim();
                    tracked.PersonalEmail = EditEmail?.Trim();

                    await _context.SaveChangesAsync();

                    // Reflect changes in the in-memory object for the list
                    SelectedMember.AnonymousName = tracked.AnonymousName;
                    SelectedMember.MobileNumber = tracked.MobileNumber;
                    SelectedMember.PersonalEmail = tracked.PersonalEmail;

                    Logger.Information("Updated member {MemberName} (ID={MemberId})",
                        tracked.AnonymousName, tracked.Id);
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
    /// Remove the selected member from the local database.
    /// Note: This is a local-only change. The next Unity sync will restore
    /// the member if they still exist in the Unity API.
    /// </summary>
    [RelayCommand]
    private async Task RemoveMember(Member member)
    {
        if (member == null) return;

        bool confirmed = await Shell.Current.DisplayAlert(
            "Remove Member",
            $"Remove \"{member.AnonymousName}\" from this group?\n\nThis is a local change and will be reverted on the next data sync.",
            "Remove", "Cancel");

        if (!confirmed) return;

        try
        {
            var tracked = await _context.Members.FindAsync(member.Id);
            if (tracked != null)
            {
                _context.Members.Remove(tracked);
                await _context.SaveChangesAsync();
            }

            ActiveMembers.Remove(member);
            _group?.Members.Remove(member);

            // If we were editing this member, close the form
            if (SelectedMember?.Id == member.Id)
            {
                IsEditing = false;
                IsCreatingNew = false;
                SelectedMember = null;
            }

            RefreshMemberCountText();
            UpdateHasActiveMembers();

            Logger.Information("Removed member {MemberName} (ID={MemberId}) from local database",
                member.AnonymousName, member.Id);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to remove member {MemberId}", member.Id);
            await Shell.Current.DisplayAlert("Error", $"Failed to remove member: {ex.Message}", "OK");
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

        if (_group?.Members != null)
        {
            foreach (var member in _group.Members.Where(m => m.IsGsr))
            {
                ActiveMembers.Add(member);
            }
        }

        UpdateHasActiveMembers();
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

        HasUnsavedChanges = SelectedMember.AnonymousName != EditName?.Trim() ||
                            SelectedMember.MobileNumber != EditPhone?.Trim() ||
                            SelectedMember.PersonalEmail != EditEmail?.Trim();
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