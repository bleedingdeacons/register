using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;
using Member = TheBleedingDeacons.Unity.Intergroup.Entities.Member;
using Position = TheBleedingDeacons.Unity.Intergroup.Entities.Position;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the editable position-holder information form.
/// Member-centric: displays a list of ALL holders for the position, allows
/// editing each one and adding new members.
///
/// Writes go to the local <see cref="UnityDbContext"/> only. The
/// <see cref="ReconciliationService"/> detects changes via snapshot
/// diffing and pushes them to the Unity API at reconciliation time.
///
/// Holder removals are staged locally and only committed when the user
/// taps OK. Tapping Cancel reverts all pending removals.
///
/// Separated from the verify flow (see <see cref="VerifyPositionViewModel"/>)
/// so each ViewModel handles a single responsibility.
///
/// Mirrors the <see cref="EditGroupViewModel"/> pattern for consistency.
/// </summary>
public partial class PositionEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<PositionEditViewModel>();

    private readonly IPositionRepository _positionRepository;
    private readonly UnityDbContext _context;
    private readonly IPopupNotification _popupService;

    // The position whose holders are being managed
    private Position? _position;

    /// <summary>
    /// The currently selected member being edited (null when not editing).
    /// </summary>
    [ObservableProperty]
    private Member? selectedMember;

    /// <summary>
    /// Observable list of active holders for the position.
    /// </summary>
    public ObservableCollection<Member> ActiveHolders { get; } = new();

    /// <summary>
    /// Holders that have been removed in this session but not yet committed.
    /// Displayed with a strikethrough / deleted card style so the user can
    /// see what will be removed when they tap OK.
    /// </summary>
    public ObservableCollection<Member> PendingRemovals { get; } = new();

    // ── Editing fields (bound to the form when a member is selected) ──

    [ObservableProperty]
    private string? editName;

    [ObservableProperty]
    private string? editPhone;

    [ObservableProperty]
    private string? editEmail;

    /// <summary>
    /// The rotation date for the position holder. Required when creating a new holder.
    /// Bound to a DatePicker on the form.
    /// </summary>
    [ObservableProperty]
    private DateTime editRotationDate = DateTime.Today;

    /// <summary>
    /// Tracks whether the user has explicitly chosen a rotation date.
    /// The DatePicker always has a value, so we need a separate flag to
    /// know if the user has interacted with it vs. just seeing the default.
    /// Set to <c>true</c> when loading an existing member's date or when
    /// the user changes the picker.
    /// </summary>
    [ObservableProperty]
    private bool rotationDateSelected;

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
    private bool hasActiveHolders;

    [ObservableProperty]
    private string holderCountText = string.Empty;

    /// <summary>
    /// True when there are pending removals that have not been committed.
    /// </summary>
    [ObservableProperty]
    private bool hasPendingRemovals;

    /// <summary>
    /// True when the page has any uncommitted work (removals, new holders, edits).
    /// Used to decide whether navigating away requires confirmation.
    /// </summary>
    [ObservableProperty]
    private bool hasPendingChanges;

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

    [ObservableProperty]
    private string? rotationDateError;

    [ObservableProperty]
    private bool hasRotationDateError;

    public PositionEditViewModel(
        IPositionRepository positionRepository,
        UnityDbContext context,
        IPopupNotification popupService)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _popupService = popupService;

        ValidateForm();
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("PositionEditViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        if (query.TryGetValue("position", out var positionObj) && positionObj is Position position)
        {
            _position = position;
            Logger.Information("Edit mode: position {PositionName} with {HolderCount} holders",
                position.ShortDescription, position.Holders.Count);

            PendingRemovals.Clear();
            HasPendingChanges = false;
            UpdateHasPendingRemovals();
            PopulateHolderList();
            UpdateTitle();
        }
        else if (query.TryGetValue("positionId", out var positionIdObj) &&
                 positionIdObj is string positionIdStr &&
                 int.TryParse(positionIdStr, out var positionId))
        {
            LoadPositionByIdAsync(positionId).SafeFireAndForget("LoadPositionById");
        }
    }

    #endregion

    #region Initialization

    private async Task LoadPositionByIdAsync(int positionId)
    {
        try
        {
            var position = await _positionRepository.GetByIdWithHoldersAsync(positionId);
            if (position != null)
            {
                _position = position;
                PendingRemovals.Clear();
                HasPendingChanges = false;
                UpdateHasPendingRemovals();
                PopulateHolderList();
                UpdateTitle();
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", "Position not found.", "OK");
                await Shell.Current.GoToAsync("//MainPage");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load position {PositionId}", positionId);
            await Shell.Current.DisplayAlert("Error", $"Failed to load position: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("//MainPage");
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

    partial void OnEditRotationDateChanged(DateTime value)
    {
        RotationDateSelected = true;
        ValidateRotationDate();
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

        // Parse the stored rotation string back to a DateTime for the DatePicker
        if (DateTime.TryParse(member.IntergroupPositionRotation, out var parsed))
        {
            EditRotationDate = parsed;
            RotationDateSelected = true;
        }
        else
        {
            EditRotationDate = DateTime.Today;
            RotationDateSelected = false;
        }

        HasUnsavedChanges = false;
        ClearAllErrors();
        ValidateForm();

        Logger.Information("Selected holder {MemberName} (ID={MemberId}) for editing",
            member.AnonymousName, member.Id);
    }

    /// <summary>
    /// Begin creating a new holder for this position.
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
        EditRotationDate = DateTime.Today;
        RotationDateSelected = false;

        HasUnsavedChanges = false;
        ClearAllErrors();
        ValidateForm();

        Logger.Information("Starting new holder creation for position {PositionName}", _position?.ShortDescription);
    }

    /// <summary>
    /// Save the currently-edited or newly-created holder.
    /// </summary>
    [RelayCommand]
    private async Task SaveMember()
    {
        if (!IsFormValid)
        {
            ValidateName();
            ValidatePhone();
            ValidateEmail();
            ValidateRotationDate();
            await Shell.Current.DisplayAlert("Validation Error", "Please fix the form errors before saving.", "OK");
            return;
        }

        if (_position == null)
        {
            Logger.Warning("SaveMember called but _position is null");
            return;
        }

        try
        {
            IsLoading = true;

            if (IsCreatingNew)
            {
                // Create a brand-new holder member for this position.
                // Assign a negative temporary ID so that:
                //  1. It cannot collide with Unity's positive WordPress post IDs.
                //  2. Multiple Register apps running simultaneously get different IDs.
                //  3. Any code can check member.IsTemporary (Id < 0) to know a
                //     CreateMember API call is required.
                var newMember = new Member
                {
                    Id = TemporaryIdGenerator.Next(),
                    IntergroupPositionId = _position.Id,
                    AnonymousName = EditName?.Trim() ?? string.Empty,
                    MobileNumber = EditPhone?.Trim(),
                    PersonalEmail = EditEmail?.Trim(),
                    IntergroupPositionRotation = EditRotationDate.ToString("yyyy-MM-dd"),
                };
                _context.Members.Add(newMember);
                await _context.SaveChangesAsync();

                // Add to the position's in-memory collection and our observable list
                _position.Holders.Add(newMember);
                ActiveHolders.Add(newMember);

                Logger.Information("Created new holder {MemberName} for position {PositionName}",
                    newMember.AnonymousName, _position.ShortDescription);
            }
            else if (SelectedMember != null && SelectedMember.Id != 0)
            {
                // Update an existing tracked member
                var tracked = await _context.Members.FindAsync(SelectedMember.Id);
                if (tracked != null)
                {
                    tracked.AnonymousName = EditName?.Trim() ?? string.Empty;
                    tracked.MobileNumber = EditPhone?.Trim();
                    tracked.PersonalEmail = EditEmail?.Trim();
                    tracked.IntergroupPositionRotation = EditRotationDate.ToString("yyyy-MM-dd");

                    await _context.SaveChangesAsync();

                    // Reflect changes in the in-memory object for the list
                    SelectedMember.AnonymousName = tracked.AnonymousName;
                    SelectedMember.MobileNumber = tracked.MobileNumber;
                    SelectedMember.PersonalEmail = tracked.PersonalEmail;
                    SelectedMember.IntergroupPositionRotation = tracked.IntergroupPositionRotation;

                    Logger.Information("Updated holder {MemberName} (ID={MemberId})",
                        tracked.AnonymousName, tracked.Id);
                }
            }

            HasUnsavedChanges = false;
            IsEditing = false;
            IsCreatingNew = false;
            SelectedMember = null;

            HasPendingChanges = true;
            RefreshHolderCountText();
            UpdateHasActiveHolders();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save holder");
            await Shell.Current.DisplayAlert("Error", $"Failed to save holder: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Stage a holder for removal. The member is removed from the active list
    /// and added to <see cref="PendingRemovals"/>. No database writes happen
    /// until the user taps OK (<see cref="Done"/>). Cancel reverts all removals.
    /// </summary>
    [RelayCommand]
    private void RemoveMember(Member member)
    {
        if (member == null) return;

        ActiveHolders.Remove(member);
        PendingRemovals.Add(member);

        // If we were editing this member, close the form
        if (SelectedMember?.Id == member.Id)
        {
            IsEditing = false;
            IsCreatingNew = false;
            SelectedMember = null;
        }

        UpdateHasPendingRemovals();
        HasPendingChanges = true;
        RefreshHolderCountText();
        UpdateHasActiveHolders();

        Logger.Information("Staged removal of holder {MemberName} (ID={MemberId})",
            member.AnonymousName, member.Id);
    }

    /// <summary>
    /// Undo a pending removal — move the member back from the pending list
    /// to the active list.
    /// </summary>
    [RelayCommand]
    private void UndoRemoveMember(Member member)
    {
        if (member == null) return;

        PendingRemovals.Remove(member);
        ActiveHolders.Add(member);

        UpdateHasPendingRemovals();
        HasPendingChanges = PendingRemovals.Count > 0;
        RefreshHolderCountText();
        UpdateHasActiveHolders();

        Logger.Information("Undid removal of holder {MemberName} (ID={MemberId})",
            member.AnonymousName, member.Id);
    }

    /// <summary>
    /// Cancel the current holder edit form without saving.
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
    /// OK — commit all pending removals to the database and navigate back.
    /// </summary>
    [RelayCommand]
    private async Task Done()
    {
        if (IsEditing && HasUnsavedChanges)
        {
            bool shouldLeave = await Shell.Current.DisplayAlert(
                "Unsaved Changes",
                "You have unsaved changes on the current holder. Discard and go back?",
                "Discard", "Keep Editing");

            if (!shouldLeave) return;
        }

        try
        {
            // Commit all pending removals to the database
            foreach (var member in PendingRemovals)
            {
                var tracked = await _context.Members.FindAsync(member.Id);
                if (tracked != null)
                {
                    if (member.IsTemporary)
                    {
                        _context.Members.Remove(tracked);
                        _position?.Holders.Remove(member);
                        Logger.Information("Deleted temporary holder {MemberName} (ID={MemberId}) from local database",
                            member.AnonymousName, member.Id);
                    }
                    else
                    {
                        tracked.IntergroupPositionId = null;
                        tracked.IntergroupPositionRotation = null;
                        Logger.Information("Cleared position association for holder {MemberName} (ID={MemberId}) for sync",
                            member.AnonymousName, member.Id);
                    }
                }
            }

            if (PendingRemovals.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            PendingRemovals.Clear();
            UpdateHasPendingRemovals();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to commit pending removals");
            await Shell.Current.DisplayAlert("Error", $"Failed to save changes: {ex.Message}", "OK");
            return;
        }

        await Shell.Current.GoToAsync($"..?edited=true");
    }

    /// <summary>
    /// Cancel — revert all pending removals and navigate back without saving.
    /// </summary>
    [RelayCommand]
    private async Task CancelPage()
    {
        if (HasPendingChanges || (IsEditing && HasUnsavedChanges))
        {
            bool shouldLeave = await Shell.Current.DisplayAlert(
                "Discard Changes",
                "You have unsaved changes. Discard and go back?",
                "Discard", "Keep Editing");

            if (!shouldLeave) return;
        }

        // Revert pending removals — move them back to ActiveHolders
        foreach (var member in PendingRemovals)
        {
            ActiveHolders.Add(member);
        }
        PendingRemovals.Clear();
        UpdateHasPendingRemovals();
        HasPendingChanges = false;

        Logger.Information("Cancelled edit page — reverted all pending removals");

        await Shell.Current.GoToAsync("..");
    }

    #endregion

    #region Private Methods

    private void PopulateHolderList()
    {
        ActiveHolders.Clear();

        if (_position?.Holders != null)
        {
            foreach (var holder in _position.Holders)
            {
                ActiveHolders.Add(holder);
            }
        }

        UpdateHasActiveHolders();
        RefreshHolderCountText();
    }

    private void UpdateHasActiveHolders()
    {
        HasActiveHolders = ActiveHolders.Count > 0;
    }

    private void UpdateHasPendingRemovals()
    {
        HasPendingRemovals = PendingRemovals.Count > 0;
    }

    private void RefreshHolderCountText()
    {
        var count = ActiveHolders.Count;
        HolderCountText = count switch
        {
            0 => "No holders — tap + to add one",
            1 => "1 holder",
            _ => $"{count} holders"
        };
    }

    private void UpdateTitle()
    {
        var baseName = !string.IsNullOrEmpty(_position?.ShortDescription)
            ? _position!.ShortDescription
            : "Edit Holders";
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
                     !HasRotationDateError &&
                     !string.IsNullOrWhiteSpace(EditName) &&
                     !string.IsNullOrWhiteSpace(EditPhone) &&
                     !string.IsNullOrWhiteSpace(EditEmail) &&
                     RotationDateSelected;
    }

    private void CheckForUnsavedChanges()
    {
        if (IsCreatingNew)
        {
            HasUnsavedChanges = !string.IsNullOrWhiteSpace(EditName) ||
                                !string.IsNullOrWhiteSpace(EditPhone) ||
                                !string.IsNullOrWhiteSpace(EditEmail) ||
                                RotationDateSelected;
            return;
        }

        if (SelectedMember == null) { HasUnsavedChanges = false; return; }

        // Parse the member's stored rotation string for comparison
        var existingRotation = DateTime.TryParse(SelectedMember.IntergroupPositionRotation, out var parsed)
            ? (DateTime?)parsed.Date
            : null;

        HasUnsavedChanges = SelectedMember.AnonymousName != EditName?.Trim() ||
                            SelectedMember.MobileNumber != EditPhone?.Trim() ||
                            SelectedMember.PersonalEmail != EditEmail?.Trim() ||
                            existingRotation?.Date != EditRotationDate.Date;
    }

    private void ClearAllErrors()
    {
        ClearNameError();
        ClearPhoneError();
        ClearEmailError();
        ClearRotationDateError();
    }

    private void SetNameError(string error) { NameError = error; HasNameError = true; }
    private void ClearNameError() { NameError = null; HasNameError = false; }
    private void SetPhoneError(string error) { PhoneError = error; HasPhoneError = true; }
    private void ClearPhoneError() { PhoneError = null; HasPhoneError = false; }
    private void SetEmailError(string error) { EmailError = error; HasEmailError = true; }
    private void ClearEmailError() { EmailError = null; HasEmailError = false; }
    private void SetRotationDateError(string error) { RotationDateError = error; HasRotationDateError = true; }
    private void ClearRotationDateError() { RotationDateError = null; HasRotationDateError = false; }

    private void ValidateRotationDate()
    {
        ClearRotationDateError();

        if (!RotationDateSelected)
            SetRotationDateError("Rotation date is required.");
    }

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