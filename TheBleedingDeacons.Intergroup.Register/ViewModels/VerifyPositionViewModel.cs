using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the read-only verification and registration flow for a position.
/// The user confirms the position holder details are correct (Yes) or navigates to edit them (No).
///
/// Displays ALL active holders for the position as a member-centric list.
///
/// Receives a positionId from navigation and loads the Position (with Holders)
/// from <see cref="IPositionRepository"/>, so verify/edit always operate on the position.
///
/// NOTE: [QueryProperty] attributes are intentionally omitted here.
/// Using [QueryProperty] alongside a manual ApplyQueryAttributes override causes
/// OnPositionIdChanged to fire twice — once from the source-generated setter
/// (before ApplyQueryAttributes runs) and again when ApplyQueryAttributes sets
/// PositionId. The second call hits the IsLoading guard and returns early,
/// leaving the holders list empty. All navigation parameter handling is done
/// exclusively in ApplyQueryAttributes instead.
/// </summary>
public partial class VerifyPositionViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<VerifyPositionViewModel>();

	private readonly IAttendanceRegistration<Position> _attendanceRegistration;
	private readonly IPositionRepository _positionRepository;
	private readonly IPopupNotification _popupService;
	private readonly IComplianceRegistration _complianceRegistration;
	private readonly IPrivacyPolicyCache _privacyPolicyCache;

	[ObservableProperty]
	private Position? position;

	[ObservableProperty]
	private int positionId;

	[ObservableProperty]
	private string attendedStatusText = string.Empty;

	[ObservableProperty]
	private bool edited;

	[ObservableProperty]
	private bool standingIn;

	[ObservableProperty]
	private string? standinEmail;

	[ObservableProperty]
	private string? standinName;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(YesCommand))]
	private bool canRegister;

	[ObservableProperty]
	private bool isLoading;

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

	/// <summary>
	/// Active holders for the position, displayed as a list.
	/// </summary>
	public ObservableCollection<Member> ActiveHolders { get; } = new();

	/// <summary>
	/// True when the position has at least one active holder to display.
	/// </summary>
	[ObservableProperty]
	private bool hasActiveHolders;

	/// <summary>
	/// Descriptive text showing how many holders are registered for this position.
	/// </summary>
	[ObservableProperty]
	private string holderCountText = string.Empty;

	public VerifyPositionViewModel(
		IAttendanceRegistration<Position> attendanceRegistration,
		IPositionRepository positionRepository,
		IPopupNotification popupService,
		IComplianceRegistration complianceRegistration,
		IPrivacyPolicyCache privacyPolicyCache)
	{
		_attendanceRegistration = attendanceRegistration;
		_positionRepository = positionRepository;
		_popupService = popupService;
		_complianceRegistration = complianceRegistration;
		_privacyPolicyCache = privacyPolicyCache;
	}

	#region Query Attributes Handling

	public override void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		Logger.Information("VerifyPositionViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

		// Handle edited flag returning from Edit flow — reload from DB so updated
		// holder values are reflected rather than the stale in-memory Position object.
		// PositionId is already set from the original navigation, so reload directly.
		if (query.TryGetValue("edited", out var editedObj) &&
			editedObj?.ToString() == "true")
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				if (PositionId > 0)
					await LoadPositionAsync(PositionId);
			});
			return;
		}

		// Initial navigation: parse positionId and trigger a single load.
		// We set PositionId for reference but call LoadPositionAsync directly rather
		// than relying on OnPositionIdChanged, which would race with this method.
		if (query.TryGetValue("positionId", out var positionIdObj))
		{
			int parsedPositionId = 0;

			// Folding the parse into the pattern uses TryParse's result rather
			// than discarding it (MA0060). Behaviour is unchanged: a string
			// that fails to parse leaves parsedPositionId at 0 and falls past
			// the int branch, which the guard below already rejects.
			if (positionIdObj is string positionIdStr && int.TryParse(positionIdStr, out var parsedFromString))
				parsedPositionId = parsedFromString;
			else if (positionIdObj is int intValue)
				parsedPositionId = intValue;

			if (parsedPositionId > 0)
			{
				PositionId = parsedPositionId;

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
					await LoadPositionAsync(parsedPositionId);
				});
			}
		}
	}

	#endregion

	#region Property Change Handlers

	partial void OnPositionIdChanged(int value)
	{
		// PositionId is set for reference only. Loading is triggered exclusively
		// from ApplyQueryAttributes to prevent double-load races.
		Logger.Information("OnPositionIdChanged: PositionId updated to {Value}", value);
	}

	partial void OnPositionChanged(Position? value)
	{
		if (value != null)
		{
			UpdateTitle();
			RefreshActiveHolders();
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
		if (Position == null)
		{
			Logger.Warning("Cannot navigate to edit - Position is null");
			return;
		}

		var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["position"] = Position
		};

		await Shell.Current.GoToAsync(nameof(EditPositionPage), parameters);
	}

	/// <summary>
	/// User confirms details are correct — register attendance for the position.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanExecuteYes))]
	public async Task Yes()
	{
		if (Position == null)
		{
			Logger.Warning("Cannot register - Position is null");
			return;
		}

		try
		{
			// GDPR gate. Any active holder who has not previously accepted
			// the privacy policy — OR whose recorded acceptance is for an
			// earlier version than the currently cached active policy —
			// must do so now before their data is committed as a registered
			// attendance. The popup is shown once per holder with that
			// holder's name in the title (mirrors VerifyGroupViewModel) —
			// declining for any holder aborts the registration silently.
			// Accepting records (or refreshes) that holder's acceptance at
			// the current version before moving on to the next.
			//
			// The cached version comparison is intentionally an inequality
			// check, not a "less than" check: PrivacyPolicy.Version is
			// free-form text per the Scrutiny contract, so any differing
			// recorded version is treated as out-of-date for the purposes
			// of re-prompting. If the cache is missing, fall back to the
			// "never accepted" filter only — PromptForComplianceAsync's
			// own null-cache guard will then surface the right error.
			var cachedVersion = _privacyPolicyCache.GetCached()?.Version;
			var unaccepted = ActiveHolders.Where(m =>
				m.GdprAccepted != true
				|| (!string.IsNullOrWhiteSpace(cachedVersion)
					&& !string.Equals(m.GdprAcceptanceVersion, cachedVersion, StringComparison.Ordinal)))
				.ToList();
			if (unaccepted.Count > 0)
			{
				var consentGiven = await PromptForComplianceAsync(unaccepted);
				if (!consentGiven)
				{
					Logger.Information(
						"Position {Position} registration aborted: GDPR consent declined for {Count} holder(s)",
						Position.ShortDescription, unaccepted.Count);
					return;
				}
			}

			await _attendanceRegistration.Register(Position);

			await _popupService.ShowCountdownPopupAsync(
				"Registered",
				$"Welcome {Position.ShortDescription}",
				async () =>
				{
					// Symmetric with VerifyGroupViewModel.Yes() — return to
					// the Registrations overview when that's where we came
					// from, so its OnAppearing reload re-evaluates the row.
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

	/// <summary>
	/// Shows the compliance popup once per supplied position holder, with
	/// that holder's name in the title so it's unambiguous whose consent
	/// is being captured. Each acceptance is recorded individually via
	/// <see cref="IComplianceRegistration.RecordAcceptance"/> as soon as
	/// it's given. Returns <c>true</c> only when every holder accepted;
	/// returns <c>false</c> as soon as any holder declines (or the popup
	/// is dismissed without an explicit choice), without prompting the
	/// remaining holders — the overall registration cannot proceed.
	///
	/// Per-holder acceptance timestamps are captured at the moment the
	/// user clicks Accept for that holder, rather than sharing a single
	/// batch timestamp, so the audit trail reflects the actual sequence
	/// of consent events. Mirrors the per-member pattern in
	/// <see cref="VerifyGroupViewModel"/>.
	/// </summary>
	private async Task<bool> PromptForComplianceAsync(IEnumerable<Member> members)
	{
		// Read the cached active policy first. The sync-stage gate
		// guarantees this is populated before a meeting can start, so
		// reaching this method with an empty cache means the sync was
		// bypassed or a sync cleared the cache because Scrutiny had
		// no active policy. Either way, refuse to record consent —
		// recording an acceptance with no version would corrupt the
		// audit trail. (See VerifyGroupViewModel for the equivalent
		// guard on the group-registration flow.)
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

		// The body shown in the popup comes from the cached upstream
		// policy now that the bundled Terms.txt has been retired. An
		// empty body means the upstream policy was published without
		// prose filled in — surfacing an empty popup with an "I Agree"
		// button would be the worst possible audit-trail outcome
		// ("agreed to nothing"), so refuse to prompt and route the
		// operator to a re-sync, mirroring the missing-cache branch.
		// (Same guard as VerifyGroupViewModel.)
		if (string.IsNullOrWhiteSpace(cachedPolicy.Policy))
		{
			Logger.Error(
				"Cached privacy policy {PolicyId} v{Version} has empty body; refusing to prompt for consent",
				cachedPolicy.Id, cachedPolicy.Version);
			await _popupService.ShowErrorAsync(
				"Cannot record consent",
				"The cached privacy policy has no body text on record. " +
				"Re-sync from the Admin page before continuing.");
			return false;
		}

		var policyBody = cachedPolicy.Policy;

		foreach (var member in members)
		{
			// Compose a per-holder title so the user can see which
			// holder's consent the popup is asking for. Both the title
			// and the body come from the cached Scrutiny record —
			// Scrutiny is the single source of truth for everything the
			// user sees, the audit trail records, and the confirmation
			// email quotes. Mirrors the per-member title pattern in
			// VerifyGroupViewModel so the position flow is consistent
			// with the group flow.
			string memberName = !string.IsNullOrWhiteSpace(member.AnonymousName)
				? member.AnonymousName
				: "this position holder";
			string perMemberTitle = $"{cachedPolicy.Title} — {memberName}";

			bool accepted = await _popupService.ShowTerms(perMemberTitle, policyBody);
			if (!accepted)
			{
				Logger.Information(
					"GDPR consent declined for holder {MemberId} ({Name}); aborting position registration",
					member.Id, member.AnonymousName);
				return false;
			}

			// Record this holder's acceptance immediately. Per-holder
			// timestamps mean each row in the audit log carries the
			// real moment the user clicked Accept for that holder,
			// rather than a single shared batch timestamp.
			//
			// Version is the cached Scrutiny version. The `statement`
			// parameter is no longer used by ComplianceService (it
			// sources the wording from the cache itself) but is kept
			// on the call for ABI continuity. See the equivalent block
			// in VerifyGroupViewModel for the full rationale.
			var ts = DateTime.UtcNow;
			try
			{
				await _complianceRegistration.RecordAcceptance(
					member,
					version: cachedPolicy.Version,
					statement: policyBody,
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
				// Per-holder persistence failure is logged but doesn't
				// abort the loop — the DB write inside ComplianceService
				// already swallows its own errors, so a throw here would
				// be unusual. The user did consent in the UI, so we still
				// honour that and continue prompting the remaining holders.
				Logger.Warning(ex,
					"Failed to record GDPR acceptance for holder {MemberId} ({Name})",
					member.Id, member.AnonymousName);
			}
		}

		return true;
	}

	private async Task LoadPositionAsync(int positionId)
	{
		Logger.Information("LoadPositionAsync called with positionId: {PositionId}", positionId);

		if (IsLoading) return;

		try
		{
			IsLoading = true;

			var loadedPosition = await _positionRepository.GetByIdWithHoldersAsync(positionId);

			if (loadedPosition != null)
			{
				Position = loadedPosition;
			}
			else
			{
				Logger.Warning("Position not found for ID: {PositionId}", positionId);
				var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
				if (mainPage != null)
				{
					await mainPage.DisplayAlertAsync("Not Found", $"Position with ID {positionId} was not found.", "OK");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to load position {PositionId}", positionId);

			try
			{
				var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
				if (mainPage != null)
				{
					await mainPage.DisplayAlertAsync("Error", $"Failed to load position: {ex.Message}", "OK");
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
	/// Rebuild the observable list of active holder members from the loaded Position.
	/// Mirrors <see cref="VerifyGroupViewModel.RefreshActiveGsrs"/> for consistency.
	/// </summary>
	private void RefreshActiveHolders()
	{
		ActiveHolders.Clear();

		if (Position?.Holders != null)
		{
			foreach (var holder in Position.Holders)
			{
				ActiveHolders.Add(holder);
			}
		}

		HasActiveHolders = ActiveHolders.Count > 0;

		var count = ActiveHolders.Count;
		HolderCountText = count switch
		{
			0 => "No Position Holders",
			1 => "1 Position Holder",
			_ => $"{count} Position Holders"
		};
	}

	private void UpdateTitle()
	{
		Title = !string.IsNullOrEmpty(Position?.ShortDescription)
			? Position.ShortDescription
			: "Position Verification";
	}

	private void UpdateCanRegister()
	{
		// At least one active holder must have required contact fields
		CanRegister = ActiveHolders.Any(h =>
			!string.IsNullOrEmpty(h.AnonymousName) &&
			(!string.IsNullOrEmpty(h.MobileNumber) || !string.IsNullOrEmpty(h.PersonalEmail)));
	}

	/// <summary>
	/// Authoritative guard for the Yes command. Mirrors the CanRegister invariant so the
	/// button cannot fire even if IsEnabled propagation misbehaves. Wired via
	/// [NotifyCanExecuteChangedFor(nameof(YesCommand))] on CanRegister, so any change
	/// re-runs this automatically and the button's Disabled visual state updates.
	/// </summary>
	private bool CanExecuteYes()
	{
		return ActiveHolders.Any(h =>
			!string.IsNullOrEmpty(h.AnonymousName) &&
			(!string.IsNullOrEmpty(h.MobileNumber) || !string.IsNullOrEmpty(h.PersonalEmail)));
	}

	#endregion
}