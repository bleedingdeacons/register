using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using Group = TheBleedingDeacons.Unity.Intergroup.Entities.Group;
using Member = TheBleedingDeacons.Unity.Intergroup.Entities.Member;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the editable GSR information form for a group.
/// Member-centric: displays a list of ALL GSRs for the group, allows
/// editing each one and adding new members.
///
/// Writes go to the local <see cref="UnityDbContext"/> only. The
/// <see cref="ReconciliationService"/> detects changes via snapshot
/// diffing and pushes them to the Unity API at reconciliation time.
///
/// Member removals are staged locally and only committed when the user
/// taps OK. Tapping Cancel reverts all pending removals.
///
/// Separated from the verify flow (see <see cref="VerifyGroupViewModel"/>)
/// so each ViewModel handles a single responsibility.
/// </summary>
public partial class EditGroupViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<EditGroupViewModel>();

	private readonly IDbContextFactory<UnityDbContext> _contextFactory;
	private readonly IPopupNotification _popupService;

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

	/// <summary>
	/// Members that have been removed in this session but not yet committed.
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

	/// <summary>
	/// True when there are pending removals that have not been committed.
	/// </summary>
	[ObservableProperty]
	private bool hasPendingRemovals;

	/// <summary>
	/// True when the page has any uncommitted work (removals, new members, edits).
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

	public EditGroupViewModel(
		IDbContextFactory<UnityDbContext> contextFactory,
		IPopupNotification popupService)
	{
		_contextFactory = contextFactory;
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
			Logger.Information("Edit mode: group {GroupName} with {MemberCount} members",
				group.Name, group.Members.Count);

			PendingRemovals.Clear();
			HasPendingChanges = false;
			UpdateHasPendingRemovals();
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

			using var context = _contextFactory.CreateDbContext();

			if (IsCreatingNew)
			{
				// Create a brand-new GSR member for this group.
				// Assign a negative temporary ID so that:
				//  1. It cannot collide with Unity's positive WordPress post IDs.
				//  2. Multiple Register apps running simultaneously get different IDs.
				//  3. Any code can check member.IsTemporary (Id < 0) to know a
				//     CreateMember API call is required.
				var newMember = new Member
				{
					Id = TemporaryIdGenerator.Next(),
					HomeGroupId = _group.Id,
					AnonymousName = EditName?.Trim() ?? string.Empty,
					MobileNumber = EditPhone?.Trim(),
					PersonalEmail = EditEmail?.Trim(),
					IsGsr = true,
				};
				context.Members.Add(newMember);
				await context.SaveChangesAsync(Token);

				// Add to the group's in-memory collection and our observable list
				_group.Members.Add(newMember);
				ActiveMembers.Add(newMember);

				Logger.Information("Created new member {MemberName} for group {GroupName}",
					newMember.AnonymousName, _group.Name);
			}
			else if (SelectedMember != null && SelectedMember.Id != 0)
			{
				// Update an existing tracked member
				var tracked = await context.Members.FindAsync(new object[] { SelectedMember.Id }, Token);
				if (tracked != null)
				{
					tracked.AnonymousName = EditName?.Trim() ?? string.Empty;
					tracked.MobileNumber = EditPhone?.Trim();
					tracked.PersonalEmail = EditEmail?.Trim();

					await context.SaveChangesAsync(Token);

					// Reflect changes in the in-memory object
					SelectedMember.AnonymousName = tracked.AnonymousName;
					SelectedMember.MobileNumber = tracked.MobileNumber;
					SelectedMember.PersonalEmail = tracked.PersonalEmail;

					// Member doesn't implement INotifyPropertyChanged, so the
					// CollectionView won't pick up the property changes above.
					// Replace the item in the ObservableCollection to trigger a
					// CollectionChanged notification that refreshes the UI.
					var index = ActiveMembers.IndexOf(SelectedMember);
					if (index >= 0)
					{
						ActiveMembers[index] = SelectedMember;
					}

					Logger.Information("Updated member {MemberName} (ID={MemberId})",
						tracked.AnonymousName, tracked.Id);
				}
			}

			HasUnsavedChanges = false;
			IsEditing = false;
			IsCreatingNew = false;
			SelectedMember = null;

			HasPendingChanges = true;
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
	/// Stage a member for removal. The member is removed from the active list
	/// and added to <see cref="PendingRemovals"/>. No database writes happen
	/// until the user taps OK (<see cref="Done"/>). Cancel reverts all removals.
	/// </summary>
	[RelayCommand]
	private void RemoveMember(Member member)
	{
		if (member == null) return;

		ActiveMembers.Remove(member);
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
		RefreshMemberCountText();
		UpdateHasActiveMembers();

		Logger.Information("Staged removal of member {MemberName} (ID={MemberId})",
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
		ActiveMembers.Add(member);

		UpdateHasPendingRemovals();
		HasPendingChanges = PendingRemovals.Count > 0;
		RefreshMemberCountText();
		UpdateHasActiveMembers();

		Logger.Information("Undid removal of member {MemberName} (ID={MemberId})",
			member.AnonymousName, member.Id);
	}

	/// <summary>
	/// Cancel the current member edit form without saving.
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
				"You have unsaved changes on the current member. Discard and go back?",
				"Discard", "Keep Editing");

			if (!shouldLeave) return;
		}

		try
		{
			using var context = _contextFactory.CreateDbContext();

			// Commit all pending removals to the database
			foreach (var member in PendingRemovals)
			{
				var tracked = await context.Members.FindAsync(new object[] { member.Id }, Token);
				if (tracked != null)
				{
					if (member.IsTemporary)
					{
						context.Members.Remove(tracked);
						_group?.Members.Remove(member);
						Logger.Information("Deleted temporary member {MemberName} (ID={MemberId}) from local database",
							member.AnonymousName, member.Id);
					}
					else
					{
						tracked.IsGsr = false;
						Logger.Information("Set IsGsr=false for member {MemberName} (ID={MemberId}) for sync",
							member.AnonymousName, member.Id);
					}
				}
			}

			if (PendingRemovals.Count > 0)
			{
				await context.SaveChangesAsync(Token);
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

		// Revert pending removals — move them back to ActiveMembers
		foreach (var member in PendingRemovals)
		{
			ActiveMembers.Add(member);
		}
		PendingRemovals.Clear();
		UpdateHasPendingRemovals();
		HasPendingChanges = false;

		Logger.Information("Cancelled edit page — reverted all pending removals");

		await Shell.Current.GoToAsync("..");
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

	private void UpdateHasPendingRemovals()
	{
		HasPendingRemovals = PendingRemovals.Count > 0;
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