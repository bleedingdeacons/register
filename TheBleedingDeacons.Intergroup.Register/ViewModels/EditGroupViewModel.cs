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
	private readonly IPhoneNumberService _phoneService;
	private readonly IConfigurationService _configService;
	private readonly IAttendanceRegistration<Group> _groupAttendance;

	// The group whose GSRs are being managed
	private Group? _group;

	/// <summary>
	/// Snapshot of every (Id, AnonymousName) pair in the database, loaded
	/// once when a group is opened. Used by <see cref="ValidateName"/>
	/// to enforce uniqueness of AnonymousName across ALL members (not just
	/// members of the current group), without hitting the DB on every
	/// keystroke.
	///
	/// Null until the snapshot has loaded. While null, ValidateName falls
	/// back to checking only the in-memory member list — a possibly-missed
	/// clash that the next keystroke (after the snapshot lands) will catch.
	/// </summary>
	private List<(int Id, string Name)>? _allMemberNamesSnapshot;

	// True when this page was opened via the single-GSR shortcut from the
	// verify flow (editMember param). On Done we forward an autoRegister
	// flag back to the verify page so the user doesn't have to tap Yes
	// again — saving the only GSR's details and pressing Finished is a
	// strong enough signal that they want to register attendance now.
	// Cancel deliberately does NOT honour this — bailing out of the edit
	// must not auto-register.
	private bool _enteredViaSingleGsrShortcut;

	/// <summary>
	/// The currently selected member being edited (null when not editing).
	/// </summary>
	[ObservableProperty]
	private Member? selectedMember;

	/// <summary>
	/// Single display-ordered list shown in the page's CollectionView. Active
	/// members appear first in the order returned by the database, with any
	/// staged-for-removal cards appended at the end. The card variant
	/// (strikethrough + Undo vs. Edit/Remove) is driven from
	/// <see cref="MemberCardItem.IsPending"/> via a XAML DataTrigger.
	///
	/// All list-mutating commands (Add / Remove / Undo) operate on this
	/// collection. The previous <c>ActiveMembers</c> / <c>PendingRemovals</c>
	/// split lives on as enumerable views over this same source —
	/// <see cref="ActiveMembers"/> and <see cref="PendingRemovals"/> below —
	/// for the Done/Cancel commit-and-revert paths that need to iterate one
	/// half or the other.
	/// </summary>
	public ObservableCollection<MemberCardItem> DisplayedMembers { get; } = new();

	/// <summary>
	/// Active (not-staged-for-removal) members, derived from <see cref="DisplayedMembers"/>.
	/// </summary>
	public IEnumerable<Member> ActiveMembers => DisplayedMembers.Where(i => !i.IsPending).Select(i => i.Member);

	/// <summary>
	/// Members staged for removal, derived from <see cref="DisplayedMembers"/>.
	/// Iterated by <see cref="Done"/> to commit removals and by
	/// <see cref="CancelPage"/> to revert them.
	/// </summary>
	public IEnumerable<Member> PendingRemovals => DisplayedMembers.Where(i => i.IsPending).Select(i => i.Member);

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
		IPopupNotification popupService,
		IPhoneNumberService phoneService,
		IConfigurationService configService,
		IAttendanceRegistration<Group> groupAttendance)
	{
		_contextFactory = contextFactory;
		_popupService = popupService;
		_phoneService = phoneService;
		_configService = configService;
		_groupAttendance = groupAttendance;

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

			// Reset the shortcut flag for this fresh entry. It will be
			// re-set below if the navigation included an editMember param.
			_enteredViaSingleGsrShortcut = false;

			RemoveAllPendingFromDisplayed();
			HasPendingChanges = false;
			UpdateHasPendingRemovals();
			PopulateMemberLists();
			UpdateTitle();
			LoadAllMemberNamesSnapshotAsync().SafeFireAndForget("LoadAllMemberNamesSnapshot");
		}

		if (query.TryGetValue("addMember", out var addObj) &&
		addObj is bool addMember && addMember)
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				// Replace this with whatever your existing Add button does
				await AddNewMember();
			});
		}

		// When the verify flow detects exactly one GSR and the user reports
		// the details aren't correct, it forwards that single member here so
		// we can skip the picker step and open the edit form directly. We go
		// through SelectMemberCommand rather than touching state by hand so
		// validation, error-clearing and HasUnsavedChanges all behave the
		// same as a normal tap on the member row.
		if (query.TryGetValue("editMember", out var editObj) &&
			editObj is Member memberToEdit)
		{
			_enteredViaSingleGsrShortcut = true;

			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (SelectMemberCommand.CanExecute(memberToEdit))
					SelectMemberCommand.Execute(memberToEdit);
			});
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
	private async Task SelectMember(Member member)
	{
		if (member == null) return;

		await ShowFeedback();

		SelectedMember = member;
		IsCreatingNew = false;
		IsEditing = true;

		// Clear errors BEFORE assigning field values. The [ObservableProperty]
		// setters trigger ValidateName/Phone/Email via OnEditXChanged, and we
		// want those validations to stick rather than being wiped afterwards.
		ClearAllErrors();

		EditName = member.AnonymousName;
		EditPhone = member.MobileNumber;
		EditEmail = member.PersonalEmail;

		// Run validators explicitly — the change-notification setters don't
		// fire if the new value equals the current value (e.g. editing a
		// second member whose fields match the previous one), so rely on
		// explicit calls rather than the OnEditXChanged side-effects.
		ValidateName();
		ValidatePhone();
		ValidateEmail();

		HasUnsavedChanges = false;
		ValidateForm();

		Logger.Information("Selected member {MemberName} (ID={MemberId}) for editing",
			member.AnonymousName, member.Id);
	}

	/// <summary>
	/// Begin creating a new member for this group.
	/// </summary>
	[RelayCommand]
	private async Task AddNewMember()
	{
		await ShowFeedback();

		SelectedMember = null;
		IsCreatingNew = true;
		IsEditing = true;

		// Clear errors BEFORE assigning field values — same ordering rationale
		// as SelectMember.
		ClearAllErrors();

		EditName = string.Empty;
		EditPhone = string.Empty;
		EditEmail = string.Empty;

		// Explicitly run each field validator. On a blank form the OnEditXChanged
		// handlers may not fire (setters short-circuit when the value already
		// equals the assigned value — common on a freshly-loaded page where the
		// backing fields are already null/empty), so the required-field error
		// flags must be set directly. Without this, IsFormValid would evaluate
		// to true on an empty form and the Save button would incorrectly enable.
		ValidateName();
		ValidatePhone();
		ValidateEmail();

		HasUnsavedChanges = false;
		ValidateForm();

		Logger.Information("Starting new member creation for group {GroupName}", _group?.Name);
	}

	/// <summary>
	/// Whether the Save button can execute. Gated on form validity and
	/// not-currently-saving so the button is visibly disabled while
	/// validation errors exist (rather than popping a dialog on click).
	/// </summary>
	private bool CanSaveMember() => IsFormValid && !IsLoading;

	/// <summary>
	/// Save the currently-edited or newly-created member.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanSaveMember))]
	private async Task SaveMember()
	{
		// Defensive guard — the CanExecute gate should prevent this path,
		// but if the command is somehow invoked while invalid we silently
		// no-op rather than showing a dialog.
		if (!IsFormValid) return;

		if (_group == null)
		{
			Logger.Warning("SaveMember called but _group is null");
			return;
		}

		try
		{
			await ShowFeedback();

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

				// Add to the group's in-memory collection and our observable list.
				// New members are inserted before any pending-removal cards so the
				// "actives first, removals appended" ordering is preserved.
				_group.Members.Add(newMember);
				InsertActiveDisplayed(newMember);

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
					// Replace the wrapper in DisplayedMembers to trigger a
					// CollectionChanged notification that refreshes the UI.
					var index = IndexOfDisplayed(SelectedMember);
					if (index >= 0)
					{
						DisplayedMembers[index] = MemberCardItem.Active(SelectedMember);
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
	private async Task RemoveMember(Member member)
	{
		if (member == null) return;

		await ShowFeedback();

		// Remove the active wrapper and append a pending wrapper at the end of
		// the displayed list. The "actives first, removals at the end" ordering
		// is what makes the single combined CollectionView readable.
		var idx = IndexOfDisplayed(member);
		if (idx >= 0)
		{
			DisplayedMembers.RemoveAt(idx);
		}
		DisplayedMembers.Add(MemberCardItem.Pending(member));

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
	private async Task UndoRemoveMember(Member member)
	{
		if (member == null) return;

		await ShowFeedback();

		// Drop the pending wrapper and re-insert as an active card at the end
		// of the active block (i.e. just before the first remaining pending
		// card, or at the end if there are no others).
		var idx = IndexOfDisplayed(member);
		if (idx >= 0)
		{
			DisplayedMembers.RemoveAt(idx);
		}
		InsertActiveDisplayed(member);

		UpdateHasPendingRemovals();
		// Undoing a removal is itself a pending change (we're putting the
		// member back after a delete was staged), and it doesn't cancel any
		// prior adds or edits — so keep the flag set regardless of whether
		// there are still other pending removals in the list.
		HasPendingChanges = true;
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

		await ShowFeedback();

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
	/// Finished — commit all pending removals to the database and navigate
	/// back, signalling the parent page (via ?edited=true) that member-list
	/// state may have changed so it can refresh.
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
			await ShowFeedback();

			using var context = _contextFactory.CreateDbContext();

			// Snapshot the pending list before any DB work — committing iterates
			// it once, and we'll remove the wrappers from DisplayedMembers after
			// SaveChanges succeeds. Materialising avoids re-evaluating the
			// derived enumerable while we mutate the source.
			var pendingSnapshot = PendingRemovals.ToList();

			// Commit all pending removals to the database
			foreach (var member in pendingSnapshot)
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

			if (pendingSnapshot.Count > 0)
			{
				await context.SaveChangesAsync(Token);
			}

			RemoveAllPendingFromDisplayed();
			UpdateHasPendingRemovals();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to commit pending removals");
			await Shell.Current.DisplayAlert("Error", $"Failed to save changes: {ex.Message}", "OK");
			return;
		}

		// If all members have been removed, unregister the group and return
		// to the main page — there is nothing left to verify or register.
		if (!HasActiveMembers && _group is { Registered: true })
		{
			try
			{
				await _groupAttendance.Unregister(_group);
				Logger.Information(
					"All members removed from group {Name} — unregistered and returning to main page",
					_group.Name);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to unregister empty group {Name}", _group.Name);
			}

			await Shell.Current.GoToAsync("//MainPage");
			return;
		}

		// When this edit was entered via the single-GSR shortcut AND the
		// shortcut is still enabled in Settings, signal to the verify page
		// that it should auto-fire the Yes/register command on return.
		// CanExecuteYes still gates the actual registration, so if validation
		// somehow fails the user just lands back on the verify page with the
		// Yes button enabled or disabled as normal. Re-checking the toggle
		// here (rather than relying solely on the producer-side gate in
		// VerifyGroupViewModel.No) keeps the on/off behaviour consistent
		// even if a future caller sets _enteredViaSingleGsrShortcut without
		// going through the verify flow.
		var autoRegister = _enteredViaSingleGsrShortcut &&
						   _configService.IsSingleGsrShortcutEnabled;

		var returnRoute = autoRegister
			? "..?edited=true&autoRegister=true"
			: "..?edited=true";

		await Shell.Current.GoToAsync(returnRoute);
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

		await ShowFeedback();

		// Revert pending removals — flip each pending card back to an active
		// card, in place. Iterate over a snapshot so we can mutate the
		// underlying collection without invalidating the loop.
		for (var i = 0; i < DisplayedMembers.Count; i++)
		{
			if (DisplayedMembers[i].IsPending)
			{
				DisplayedMembers[i] = MemberCardItem.Active(DisplayedMembers[i].Member);
			}
		}
		UpdateHasPendingRemovals();
		HasPendingChanges = false;

		Logger.Information("Cancelled edit page — reverted all pending removals");

		await Shell.Current.GoToAsync("..");
	}

	#endregion

	#region Private Methods

	/// <summary>
	/// Find the index of a member's wrapper in <see cref="DisplayedMembers"/>,
	/// regardless of whether the wrapper is currently Active or Pending.
	/// Returns -1 if the member isn't displayed.
	/// </summary>
	private int IndexOfDisplayed(Member member)
	{
		for (var i = 0; i < DisplayedMembers.Count; i++)
		{
			if (DisplayedMembers[i].Member.Id == member.Id) return i;
		}
		return -1;
	}

	/// <summary>
	/// Insert <paramref name="member"/> as an Active card immediately after the
	/// last existing Active card — i.e. at the end of the active block, just
	/// before any pending-removal cards. Preserves the "actives first, removals
	/// at the end" ordering used by the page's combined CollectionView.
	/// </summary>
	private void InsertActiveDisplayed(Member member)
	{
		var insertAt = DisplayedMembers.Count;
		for (var i = 0; i < DisplayedMembers.Count; i++)
		{
			if (DisplayedMembers[i].IsPending)
			{
				insertAt = i;
				break;
			}
		}
		DisplayedMembers.Insert(insertAt, MemberCardItem.Active(member));
	}

	/// <summary>
	/// Remove every wrapper currently marked Pending from <see cref="DisplayedMembers"/>.
	/// Used by Done (after a successful commit) and on entry, where we want a
	/// clean slate.
	/// </summary>
	private void RemoveAllPendingFromDisplayed()
	{
		for (var i = DisplayedMembers.Count - 1; i >= 0; i--)
		{
			if (DisplayedMembers[i].IsPending)
			{
				DisplayedMembers.RemoveAt(i);
			}
		}
	}

	private void PopulateMemberLists()
	{
		DisplayedMembers.Clear();

		if (_group?.Members != null)
		{
			foreach (var member in _group.Members.Where(m => m.IsGsr))
			{
				DisplayedMembers.Add(MemberCardItem.Active(member));
			}
		}

		UpdateHasActiveMembers();
		RefreshMemberCountText();
	}

	/// <summary>
	/// Loads a snapshot of every member's (Id, AnonymousName) for use by
	/// <see cref="ValidateName"/>'s uniqueness check. AsNoTracking + a
	/// projected select keeps this cheap; we don't need tracked entities.
	/// Failures are logged and swallowed: the validator falls back to
	/// checking just this group's active members, which is still useful
	/// (and the per-group behaviour pre-this-change).
	/// </summary>
	private async Task LoadAllMemberNamesSnapshotAsync()
	{
		try
		{
			using var context = _contextFactory.CreateDbContext();
			var rows = await context.Members
				.AsNoTracking()
				.Where(m => !string.IsNullOrWhiteSpace(m.AnonymousName))
				.Select(m => new { m.Id, m.AnonymousName })
				.ToListAsync();

			_allMemberNamesSnapshot = rows
				.Select(r => (r.Id, Name: r.AnonymousName!))
				.ToList();

			Logger.Information("Loaded {Count} member names for uniqueness check",
				_allMemberNamesSnapshot.Count);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to load member-name snapshot for uniqueness check");
			// Leave _allMemberNamesSnapshot null; ValidateName degrades gracefully.
		}
	}

	private void UpdateHasActiveMembers()
	{
		HasActiveMembers = DisplayedMembers.Any(i => !i.IsPending);
	}

	private void UpdateHasPendingRemovals()
	{
		HasPendingRemovals = DisplayedMembers.Any(i => i.IsPending);
	}

	private void RefreshMemberCountText()
	{
		var count = DisplayedMembers.Count(i => !i.IsPending);
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
		{
			SetNameError("Name is required.");
			return;
		}

		var trimmed = EditName.Trim();

		if (trimmed.Length > 215)
		{
			SetNameError("Name cannot exceed 215 characters.");
			return;
		}

		// Reject a name that matches ANY other member's AnonymousName, not
		// just other GSRs of this group. We compare against:
		//
		//   1. _allMemberNamesSnapshot — every member in the DB at the time
		//      this page opened. Excludes the currently-edited member (so
		//      it validates against itself cleanly) and any members staged
		//      for removal in this session (so reusing a name freed by a
		//      pending removal is allowed).
		//
		//   2. ActiveMembers — covers locally-added new GSRs that don't
		//      yet exist in the DB (their Id is negative until SaveMember
		//      persists them) and any in-session renames not yet flushed.
		//
		// If the snapshot hasn't loaded yet (very early keystroke after
		// page open), we degrade to (2)-only — the next keystroke after
		// the snapshot lands will catch any cross-group clash.
		//
		// Comparison is OrdinalIgnoreCase: matches the codebase's ordinal
		// preference while treating "alice" and "Alice" as the same name.
		var editingId = !IsCreatingNew ? SelectedMember?.Id : null;
		var pendingRemovalIds = PendingRemovals.Select(m => m.Id).ToHashSet();

		bool clashes = ActiveMembers.Any(m =>
			m.Id != editingId &&
			!string.IsNullOrWhiteSpace(m.AnonymousName) &&
			string.Equals(m.AnonymousName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

		if (!clashes && _allMemberNamesSnapshot != null)
		{
			clashes = _allMemberNamesSnapshot.Any(t =>
				t.Id != editingId &&
				!pendingRemovalIds.Contains(t.Id) &&
				string.Equals(t.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
		}

		if (clashes)
		{
			SetNameError("A member with this anonymous name already exists.");
		}
	}

	private void ValidatePhone()
	{
		ClearPhoneError();

		if (string.IsNullOrWhiteSpace(EditPhone))
			SetPhoneError("Phone number is required.");
		else if (EditPhone.Trim().Length > 20)
			SetPhoneError("Phone number cannot exceed 20 characters.");
		else if (!IsValidPhoneFormat(EditPhone.Trim()))
			SetPhoneError("Please check the phone number is valid.");
	}

	private void ValidateEmail()
	{
		ClearEmailError();

		if (string.IsNullOrWhiteSpace(EditEmail))
			SetEmailError("Email address is required.");
		else if (EditEmail.Trim().Length > 255)
			SetEmailError("Email address cannot exceed 255 characters.");
		else if (!IsValidEmail(EditEmail.Trim()))
			SetEmailError("Please check the email address is correct.");
	}

	private void ValidateForm()
	{
		// The three HasXError flags now fully cover required-ness and format,
		// so no extra IsNullOrWhiteSpace checks are needed here.
		IsFormValid = !HasNameError &&
					 !HasPhoneError &&
					 !HasEmailError;
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

	private bool IsValidPhoneFormat(string phone)
	{
		return _phoneService.Validate(phone).IsValid;
	}

	#endregion
}