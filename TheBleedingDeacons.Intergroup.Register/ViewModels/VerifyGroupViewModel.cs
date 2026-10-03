using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the read-only verification and registration flow for a group.
/// The user confirms their GSR details are correct (Yes) or navigates to edit them (No).
///
/// Displays ALL active GSRs for the group as a member-centric list.
///
/// Receives a groupId from navigation and loads the Group (with Members)
/// from <see cref="IGroupRepository"/>, so verify/edit always operate on the group.
///
/// NOTE: [QueryProperty] attributes are intentionally omitted here.
/// Using [QueryProperty] alongside a manual ApplyQueryAttributes override causes
/// OnGroupIdChanged to fire twice — once from the source-generated property setter
/// (triggered by [QueryProperty] before ApplyQueryAttributes runs) and again when
/// ApplyQueryAttributes manually sets GroupId. The second call hits the IsLoading
/// guard and exits without loading, leaving the GSR list empty. All navigation
/// parameter handling is done exclusively in ApplyQueryAttributes instead.
/// </summary>
public partial class VerifyGroupViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<VerifyGroupViewModel>();

	private readonly IAttendanceRegistration<Group> _attendanceRegistration;
	private readonly IGroupRepository _groupRepository;
	private readonly IPositionRepository _positionRepository;
	private readonly IPopupNotification _popupService;
	private readonly IConfigurationService _configService;
	private readonly IPrivacyPolicyCache _privacyPolicyCache;
	private readonly ConsentRound _consentRound;

	[ObservableProperty]
	private Group? group;

	[ObservableProperty]
	private int groupId;

	[ObservableProperty]
	private string attendedStatusText = string.Empty;

	[ObservableProperty]
	private bool edited;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(YesCommand))]
	private bool standingIn;

	[ObservableProperty]
	private string? standinEmail;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(YesCommand))]
	private string? standinName;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(YesCommand))]
	private bool canRegister;

	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private string noButtonText = "No";

	/// <summary>
	/// Identifies which page initiated this Verify flow, so that on
	/// successful register we know whether to reset to MainPage (the
	/// standard registration flow) or just pop back to the Registrations
	/// overview so its list re-evaluates with the new state.
	/// Empty / unset → MainPage behaviour (default).
	/// "overview" → pop back to RegistrationOverviewPage.
	/// </summary>
	[ObservableProperty]
	private string entrySource = string.Empty;

	// If the user toggles "Standing in", re-gate the Yes button.
	partial void OnStandingInChanged(bool value) => UpdateCanRegister();

	// And if they type/clear their name, re-gate again.
	partial void OnStandinNameChanged(string? value) => UpdateCanRegister();

	/// <summary>
	/// Active GSR members for the group, displayed as a list.
	/// </summary>
	public ObservableCollection<Member> ActiveGsrs { get; } = new();

	/// <summary>
	/// True when the group has at least one active GSR to display.
	/// </summary>
	[ObservableProperty]
	private bool hasActiveGsrs;

	/// <summary>
	/// Descriptive text showing how many GSRs are registered for this group.
	/// </summary>
	[ObservableProperty]
	private string gsrCountText = string.Empty;

	public VerifyGroupViewModel(
		IAttendanceRegistration<Group> attendanceRegistration,
		IGroupRepository groupRepository,
		IPositionRepository positionRepository,
		IPopupNotification popupService,
		IConfigurationService configService,
		IPrivacyPolicyCache privacyPolicyCache,
		ConsentRound consentRound)
	{
		_attendanceRegistration = attendanceRegistration;
		_groupRepository = groupRepository;
		_positionRepository = positionRepository;
		_popupService = popupService;
		_configService = configService;
		_privacyPolicyCache = privacyPolicyCache;
		_consentRound = consentRound;
	}

	#region Query Attributes Handling

	public override void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		Logger.Information("VerifyGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

		// Handle edited flag returning from Edit flow — reload from DB so updated
		// GSR values are reflected rather than the stale in-memory Group object.
		// GroupId is already set from the original navigation, so reload directly.
		if (query.TryGetValue("edited", out var editedObj) &&
			editedObj?.ToString() == "true")
		{
			// Pick up the optional autoRegister flag now (off the query dict,
			// while we're still on the caller's thread) so the async
			// continuation below doesn't race with a subsequent navigation
			// that mutates the same dictionary.
			bool autoRegister =
				query.TryGetValue("autoRegister", out var autoObj) &&
				autoObj?.ToString() == "true";

			MainThread.BeginInvokeOnMainThread(async () =>
			{
				if (GroupId > 0)
					await LoadGroupAsync(GroupId);

				// Single-GSR shortcut completion: after the reload has
				// refreshed ActiveGsrs and re-evaluated CanRegister, fire
				// Yes automatically if the gate allows it. CanExecute is
				// the same invariant the button itself respects, so an
				// invalid record just leaves the user on the verify page
				// with Yes disabled rather than silently failing.
				if (autoRegister && YesCommand.CanExecute(null))
				{
					await YesCommand.ExecuteAsync(null);
				}
			});
			return;
		}

		// Initial navigation: parse groupId and trigger a single load.
		// We set GroupId for reference but call LoadGroupAsync directly rather
		// than relying on OnGroupIdChanged, which would race with this method.
		if (query.TryGetValue("groupId", out var groupIdObj))
		{
			int parsedGroupId = 0;

			// Folding the parse into the pattern uses TryParse's result rather
			// than discarding it (MA0060). Behaviour is unchanged: a string
			// that fails to parse leaves parsedGroupId at 0 and falls past the
			// int branch, which the guard below already rejects.
			if (groupIdObj is string groupIdStr && int.TryParse(groupIdStr, out var parsedFromString))
				parsedGroupId = parsedFromString;
			else if (groupIdObj is int intValue)
				parsedGroupId = intValue;

			if (parsedGroupId > 0)
			{
				GroupId = parsedGroupId;

				// Capture optional entrySource so Yes() knows whether to reset
				// to MainPage or pop back to the page that opened us. Only set
				// on the initial nav; the edited-return branch above retains it.
				if (query.TryGetValue("entrySource", out var entrySourceObj) &&
					entrySourceObj is string entrySourceStr)
				{
					EntrySource = entrySourceStr;
				}

				MainThread.BeginInvokeOnMainThread(async () =>
				{
					await LoadGroupAsync(parsedGroupId);
				});
			}
		}
	}

	#endregion

	#region Property Change Handlers

	partial void OnGroupIdChanged(int value)
	{
		// GroupId is set for reference only. Loading is triggered exclusively
		// from ApplyQueryAttributes to prevent double-load races.
		Logger.Information("OnGroupIdChanged: GroupId updated to {Value}", value);
	}

	partial void OnGroupChanged(Group? value)
	{
		if (value != null)
		{
			UpdateTitle();
			RefreshActiveGsrs();
			UpdateCanRegister();
		}
	}

	#endregion

	#region Commands

	/// <summary>
	/// User indicates their details are NOT correct — navigate to edit page.
	/// </summary>
	[RelayCommand]
	public async Task No()
	{
		if (Group == null)
		{
			Logger.Warning("Cannot navigate to edit - Group is null");
			return;
		}

		var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["group"] = Group
		};

		// If no GSRs exist, skip straight to the add-member flow on the edit page.
		// If exactly one GSR exists AND the single-GSR shortcut is enabled, the
		// user has effectively already chosen which record to fix — skip the
		// picker and open that member directly for editing. With multiple GSRs
		// (or when the shortcut is disabled in Settings) we still land on the
		// list so the user can pick.
		if (!HasActiveGsrs)
		{
			parameters["addMember"] = true;
		}
		else if (ActiveGsrs.Count == 1 && _configService.IsSingleGsrShortcutEnabled)
		{
			parameters["editMember"] = ActiveGsrs[0];
		}

		await ShowFeedback();

		await Shell.Current.GoToAsync(nameof(EditGroupPage), parameters);
	}

	/// <summary>
	/// User confirms details are correct — register attendance for the group.
	/// Gated via CanExecute so the command cannot fire even if the bound IsEnabled
	/// path is somehow bypassed.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanExecuteYes))]
	public async Task Yes()
	{
		if (Group == null)
		{
			Logger.Warning("Cannot register - Group is null");
			return;
		}

		await ShowFeedback();

		try
		{
			// GDPR gate. Each active GSR who has not accepted the current
			// version of the privacy policy is asked individually, by name,
			// before their data is committed as a registered attendance. If
			// any GSR declines, the entire group registration is aborted —
			// we cannot register a group whose members haven't all consented
			// to the current version. Who needs asking is
			// RegistrationGate.NeedingConsent; the asking is ConsentRound.
			var cachedVersion = _privacyPolicyCache.GetCached()?.Version;
			var unaccepted = RegistrationGate.NeedingConsent(ActiveGsrs, cachedVersion);
			if (unaccepted.Count > 0)
			{
				var consentGiven = await _consentRound.AskAsync(unaccepted, _popupService, "this GSR");
				if (!consentGiven)
				{
					Logger.Information(
						"Group {GroupName} registration aborted: GDPR consent declined by at least one of {Count} unaccepted GSR(s)",
						Group.Name, unaccepted.Count);
					return;
				}
			}

			// When positions are auto-registered on group registration, their
			// holders from other groups are never surfaced in ActiveGsrs and
			// so never reach the GDPR gate above. Prompt them here, before
			// the group is committed, so consent is captured in the same
			// interaction that triggers their position registration.
			//
			// Only runs when the feature is enabled — if auto-register is off,
			// positions are registered independently via the overview page and
			// the verify-position flow captures consent there instead.
			if (_configService.IsAutoRegisterPositionsOnGroupEnabled)
			{
				var otherHoldersNeedingConsent = await RegistrationGate.CascadedHoldersNeedingConsentAsync(
					ActiveGsrs, _positionRepository, cachedVersion);

				if (otherHoldersNeedingConsent.Count > 0)
				{
					var consentGiven = await _consentRound.AskAsync(otherHoldersNeedingConsent, _popupService, "this GSR");
					if (!consentGiven)
					{
						Logger.Information(
							"Group {GroupName} registration aborted: GDPR consent declined by at least one of {Count} position holder(s)",
							Group.Name, otherHoldersNeedingConsent.Count);
						return;
					}
				}
			}

			// Set proxy state on entity so AttendanceService persists it
			Group.GsrProxy = StandingIn;
			Group.GsrProxyName = StandingIn ? StandinName : null;

			await _attendanceRegistration.Register(Group);

			await _popupService.ShowCountdownPopupAsync(
				"Registered",
				$"Welcome {Group.Name}",
				async () =>
				{
					// When the user reached this Verify page from the
					// Registrations overview, pop back so its OnAppearing
					// reload re-evaluates the list (registered count, the
					// row's IsToggleEnabled etc.). The standard registration
					// flow keeps the historical "reset to MainPage" exit.
					if (string.Equals(EntrySource, "overview", StringComparison.OrdinalIgnoreCase))
						await Shell.Current.GoToAsync("..");
					else
						await Shell.Current.GoToAsync("//MainPage");
				}
			);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to register attendance");

			var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
			if (mainPage != null)
			{
				await mainPage.DisplayAlertAsync("Error", $"Failed to register: {ex.Message}", "OK");
			}
		}
	}

	#endregion

	#region Private Methods

	private async Task LoadGroupAsync(int groupId)
	{
		Logger.Information("LoadGroupAsync called with groupId: {GroupId}", groupId);

		if (IsLoading) return;

		try
		{
			IsLoading = true;

			var loadedGroup = await _groupRepository.GetByIdWithMembersAsync(groupId);

			if (loadedGroup != null)
			{
				Group = loadedGroup;
			}
			else
			{
				Logger.Warning("Group not found for ID: {GroupId}", groupId);
				var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
				if (mainPage != null)
				{
					await mainPage.DisplayAlertAsync("Not Found", $"Group with ID {groupId} was not found.", "OK");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to load group {GroupId}", groupId);

			try
			{
				var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
				if (mainPage != null)
				{
					await mainPage.DisplayAlertAsync("Error", $"Failed to load group: {ex.Message}", "OK");
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

	/// <summary>
	/// Rebuild the observable list of active GSR members from the loaded Group.
	/// Unity.Data.Entities.Member uses IsGsr flag to identify GSRs.
	/// </summary>
	private void RefreshActiveGsrs()
	{
		ActiveGsrs.Clear();

		if (Group?.Members != null)
		{
			foreach (var member in Group.Members.Where(m => m.IsGsr))
			{
				ActiveGsrs.Add(member);
			}
		}

		HasActiveGsrs = ActiveGsrs.Count > 0;
		NoButtonText = HasActiveGsrs ? "No" : "Sign-up";

		var count = ActiveGsrs.Count;
		GsrCountText = count switch
		{
			0 => "No Group Service Representatives",
			1 => "1 Group Service Representative",
			_ => $"{count} Group Service Representatives"
		};
	}

	private void UpdateTitle()
	{
		Title = !string.IsNullOrEmpty(Group?.Name) ? Group.Name : "Group Service Representative";
	}

	private void UpdateCanRegister()
	{
		// A contactable GSR, and a name when someone is standing in.
		CanRegister = RegistrationGate.CanRegisterGroup(ActiveGsrs, StandingIn, StandinName);
	}

	/// <summary>
	/// Authoritative guard for the Yes command. Mirrors the CanRegister invariant so the
	/// button cannot fire even if IsEnabled propagation misbehaves — e.g. during a
	/// rebind after the checkbox toggles visibility.
	///
	/// Wired via [NotifyCanExecuteChangedFor(nameof(YesCommand))] on StandingIn and
	/// StandinName, so any change to either re-runs this automatically.
	/// </summary>
	private bool CanExecuteYes() =>
		RegistrationGate.CanRegisterGroup(ActiveGsrs, StandingIn, StandinName);

	#endregion
}