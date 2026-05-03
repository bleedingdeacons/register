using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Utilities;
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
	private readonly IPopupNotification _popupService;
	private readonly IConfigurationService _configService;
	private readonly IComplianceRegistration _complianceRegistration;
	private readonly IPrivacyPolicyCache _privacyPolicyCache;

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
		IPopupNotification popupService,
		IConfigurationService configService,
		IComplianceRegistration complianceRegistration,
		IPrivacyPolicyCache privacyPolicyCache)
	{
		_attendanceRegistration = attendanceRegistration;
		_groupRepository = groupRepository;
		_popupService = popupService;
		_configService = configService;
		_complianceRegistration = complianceRegistration;
		_privacyPolicyCache = privacyPolicyCache;
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

			if (groupIdObj is string groupIdStr)
				int.TryParse(groupIdStr, out parsedGroupId);
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

		var parameters = new Dictionary<string, object>
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
			// GDPR gate. Each active GSR who has not previously accepted
			// the privacy policy must be asked individually before their
			// data is committed as a registered attendance. The popup is
			// shown once per outstanding GSR with that GSR's name in the
			// title, so it's clear whose consent is being captured. If
			// any GSR declines, the entire group registration is aborted
			// — we cannot register a group whose members haven't all
			// consented. GSRs who accept have their acceptance recorded
			// individually as the loop progresses.
			var unaccepted = ActiveGsrs.Where(m => m.GdprAccepted != true).ToList();
			if (unaccepted.Count > 0)
			{
				var consentGiven = await PromptForComplianceAsync(unaccepted);
				if (!consentGiven)
				{
					Logger.Information(
						"Group {GroupName} registration aborted: GDPR consent declined by at least one of {Count} unaccepted GSR(s)",
						Group.Name, unaccepted.Count);
					return;
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
				await mainPage.DisplayAlert("Error", $"Failed to register: {ex.Message}", "OK");
			}
		}
	}

	#endregion

	#region Private Methods

	/// <summary>
	/// Shows the compliance popup once per supplied GSR, with that GSR's
	/// name in the title so it's unambiguous whose consent is being
	/// captured. Each acceptance is recorded individually via
	/// <see cref="IComplianceRegistration.RecordAcceptance"/> as soon as
	/// it's given. Returns <c>true</c> only when every GSR accepted;
	/// returns <c>false</c> as soon as any GSR declines (or the popup is
	/// dismissed without an explicit choice), without prompting the
	/// remaining GSRs — the overall registration cannot proceed.
	///
	/// Per-member acceptance timestamps are captured at the moment the
	/// user clicks Accept for that member, rather than sharing a single
	/// batch timestamp, so the audit trail reflects the actual sequence
	/// of consent events.
	/// </summary>
	private async Task<bool> PromptForComplianceAsync(IEnumerable<Member> members)
	{
		// Read the cached active policy first. The sync-stage gate
		// guarantees this is populated before a meeting can start, so
		// reaching this method with an empty cache means either (a) the
		// device hasn't synced at all (unusual but possible if the
		// flow is reached via a code path that bypassed sync), or
		// (b) the cache was cleared by a sync that found "no active
		// policy". Either way, the right behaviour is to refuse to
		// record consent — recording an acceptance with no version
		// would corrupt the audit trail.
		var cachedPolicy = _privacyPolicyCache.GetCached();
		if (cachedPolicy is null)
		{
			Logger.Error(
				"No cached privacy policy on device; refusing to prompt for consent. " +
				"This indicates the sync-stage gate was bypassed.");
			await _popupService.ShowErrorAsync(
				"Cannot record consent",
				"This device has no active privacy policy on record. " +
				"Re-sync from the Admin page before continuing.");
			return false;
		}

		string termsBody;
		try
		{
			termsBody = await TermsTextLoader.LoadAsync();
		}
		catch (Exception ex)
		{
			// If we can't load the policy text we cannot show a meaningful
			// dialog. Treat as "did not consent" — the safe default — and
			// log so the operator can investigate.
			Logger.Error(ex, "Failed to load compliance text; aborting consent prompt");
			return false;
		}

		foreach (var member in members)
		{
			// Compose a per-GSR title so the user can see which member's
			// consent the popup is asking for. The base title comes from
			// the cached Scrutiny record, not from Terms.txt — Scrutiny
			// is authoritative for every audit-trail and display field
			// other than the body prose.
			string memberName = !string.IsNullOrWhiteSpace(member.AnonymousName)
				? member.AnonymousName!
				: "this GSR";
			string perMemberTitle = $"{cachedPolicy.Title} — {memberName}";

			bool accepted = await _popupService.ShowTerms(perMemberTitle, termsBody);
			if (!accepted)
			{
				Logger.Information(
					"GDPR consent declined for member {MemberId} ({Name}); aborting group registration",
					member.Id, member.AnonymousName);
				return false;
			}

			// Record this member's acceptance immediately. Per-member
			// timestamps mean each row in the audit log carries the
			// real moment the user clicked Accept for that GSR, rather
			// than a single shared batch timestamp.
			//
			// Version is the cached Scrutiny version (authoritative).
			// Statement is the bundled Terms.txt body — the exact
			// prose the user just read on screen. The audit trail
			// therefore captures both "which version they accepted"
			// and "what wording they actually saw", which can drift
			// if the bundled file ships out of sync with the server.
			var ts = DateTime.UtcNow;
			try
			{
				await _complianceRegistration.RecordAcceptance(
					member,
					version: cachedPolicy.Version,
					statement: termsBody,
					method: "register-app",
					acceptedAtUtc: ts);

				// Mirror the in-memory entity so the page's bindings reflect
				// the new state immediately — ComplianceService updates a
				// freshly-loaded Member instance, not the one the VM holds.
				member.GdprAccepted = true;
				member.GdprAcceptedAt = ts;
			}
			catch (Exception ex)
			{
				// Per-member persistence failure is logged but doesn't
				// abort the loop — the DB write inside ComplianceService
				// already swallows its own errors, so a throw here would
				// be unusual. The user did consent in the UI, so we still
				// honour that and continue prompting the remaining GSRs.
				Logger.Warning(ex,
					"Failed to record GDPR acceptance for member {MemberId} ({Name})",
					member.Id, member.AnonymousName);
			}
		}

		return true;
	}

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
					await mainPage.DisplayAlert("Not Found", $"Group with ID {groupId} was not found.", "OK");
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
					await mainPage.DisplayAlert("Error", $"Failed to load group: {ex.Message}", "OK");
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
		Title = !string.IsNullOrEmpty(Group?.Name) ? Group!.Name : "Group Service Representative";
	}

	private void UpdateCanRegister()
	{
		// At least one active GSR must have required contact fields
		bool hasValidGsr = ActiveGsrs.Any(g =>
			!string.IsNullOrEmpty(g.AnonymousName) &&
			(!string.IsNullOrEmpty(g.MobileNumber) || !string.IsNullOrEmpty(g.PersonalEmail)));

		// If the user is marking themselves as standing in, they must enter a name.
		// When StandingIn is unticked, StandinName is not required.
		bool standInOk = !StandingIn || !string.IsNullOrWhiteSpace(StandinName);

		CanRegister = hasValidGsr && standInOk;
	}

	/// <summary>
	/// Authoritative guard for the Yes command. Mirrors the CanRegister invariant so the
	/// button cannot fire even if IsEnabled propagation misbehaves — e.g. during a
	/// rebind after the checkbox toggles visibility.
	///
	/// Wired via [NotifyCanExecuteChangedFor(nameof(YesCommand))] on StandingIn and
	/// StandinName, so any change to either re-runs this automatically.
	/// </summary>
	private bool CanExecuteYes()
	{
		bool hasValidGsr = ActiveGsrs.Any(g =>
			!string.IsNullOrEmpty(g.AnonymousName) &&
			(!string.IsNullOrEmpty(g.MobileNumber) || !string.IsNullOrEmpty(g.PersonalEmail)));

		bool standInOk = !StandingIn || !string.IsNullOrWhiteSpace(StandinName);

		return hasValidGsr && standInOk;
	}

	#endregion
}