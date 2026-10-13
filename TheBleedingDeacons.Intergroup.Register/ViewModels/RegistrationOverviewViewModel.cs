using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Read-and-edit overview of every group and every position in the local
/// database, with their current local Registered state clearly marked.
///
/// Purpose: when two tablets are used at the same intergroup meeting and
/// one reconciles before the other, the second tablet needs a way to see
/// what's already been registered (after its fresh re-sync pulled the
/// first tablet's pushes back) and adjust its local state before pushing
/// its own changes. This page is that view.
///
/// The ViewModel delegates all write operations to the existing
/// <see cref="IAttendanceRegistration{T}"/> services, so the full durability
/// stack — DB write, snapshot stamp, registration event log — applies
/// automatically with no duplicated logic here.
///
/// Editing is handled by navigating to the existing
/// <see cref="VerifyGroupPage"/> / <see cref="VerifyPositionPage"/>,
/// which in turn allow jumping into the edit flow. This keeps a single
/// source of truth for validation and edit UX.
///
/// The Members tab lists every member with the group and position they
/// are assigned to, and opens <see cref="MoveMemberPage"/> to move one.
/// </summary>
public partial class RegistrationOverviewViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<RegistrationOverviewViewModel>();

	private readonly IGroupRepository _groupRepository;
	private readonly IPositionRepository _positionRepository;
	private readonly IMemberRepository _memberRepository;
	private readonly IAttendanceRegistration<Group> _groupAttendance;
	private readonly IAttendanceRegistration<Position> _positionAttendance;
	private readonly IPopupNotification _popupService;

	/// <summary>
	/// Every member from the last load, unfiltered. <see cref="Members"/>
	/// is this list narrowed by <see cref="MemberSearchText"/>.
	/// </summary>
	private List<OverviewMember> _allMembers = [];

	/// <summary>
	/// Flag used to suppress the change-handlers on OverviewGroup.IsRegistered /
	/// OverviewPosition.IsRegistered while we're rebuilding the lists from the
	/// database. Without this guard the act of refreshing the page would
	/// re-register everything. Set inside LoadAsync, reset on exit.
	/// </summary>
	private bool _suppressToggleHandlers;

	public ObservableCollection<OverviewGroup> Groups { get; } = new();
	public ObservableCollection<OverviewPosition> Positions { get; } = new();
	public ObservableCollection<OverviewMember> Members { get; } = new();

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsGroupsTabSelected))]
	[NotifyPropertyChangedFor(nameof(IsPositionsTabSelected))]
	[NotifyPropertyChangedFor(nameof(IsMembersTabSelected))]
	private OverviewTab selectedTab = OverviewTab.Groups;

	public bool IsGroupsTabSelected => SelectedTab == OverviewTab.Groups;

	public bool IsPositionsTabSelected => SelectedTab == OverviewTab.Positions;

	public bool IsMembersTabSelected => SelectedTab == OverviewTab.Members;

	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private string groupsSummary = string.Empty;

	[ObservableProperty]
	private string positionsSummary = string.Empty;

	[ObservableProperty]
	private string membersSummary = string.Empty;

	[ObservableProperty]
	private string memberSearchText = string.Empty;

	public RegistrationOverviewViewModel(
		IGroupRepository groupRepository,
		IPositionRepository positionRepository,
		IMemberRepository memberRepository,
		IAttendanceRegistration<Group> groupAttendance,
		IAttendanceRegistration<Position> positionAttendance,
		IPopupNotification popupService)
	{
		_groupRepository = groupRepository;
		_positionRepository = positionRepository;
		_memberRepository = memberRepository;
		_groupAttendance = groupAttendance;
		_positionAttendance = positionAttendance;
		_popupService = popupService;
		Title = "Registrations";
	}

	// =================================================================
	// Tab switching
	// =================================================================

	[RelayCommand]
	private void ShowGroupsTab() => SelectedTab = OverviewTab.Groups;

	[RelayCommand]
	private void ShowPositionsTab() => SelectedTab = OverviewTab.Positions;

	[RelayCommand]
	private void ShowMembersTab() => SelectedTab = OverviewTab.Members;

	// =================================================================
	// Load
	// =================================================================

	/// <summary>
	/// Loads every group and position from the local DB into the two
	/// observable collections. Called from the page's OnAppearing so the
	/// list refreshes after returning from a verify/edit navigation.
	/// </summary>
	[RelayCommand]
	public async Task LoadAsync()
	{
		if (IsBusy) return;
		IsBusy = true;
		IsLoading = true;
		_suppressToggleHandlers = true;

		try
		{
			var groups = await _groupRepository.GetAllAsync(Token);
			var positions = await _positionRepository.GetAllAsync(Token);

			// Eager-load holders so we can show who's assigned to each
			// position without a second round-trip per row.
			var positionsWithHolders = new List<Position>();
			foreach (var p in positions.OrderBy(p => p.ShortDescription, StringComparer.CurrentCulture))
			{
				var full = await _positionRepository.GetByIdWithHoldersAsync(p.Id, Token) ?? p;
				positionsWithHolders.Add(full);
			}

			// Eager-load members for groups so we can show the GSR name.
			var groupsWithMembers = new List<Group>();
			foreach (var g in groups.OrderBy(g => g.Name, StringComparer.CurrentCulture))
			{
				var full = await _groupRepository.GetByIdWithMembersAsync(g.Id, Token) ?? g;
				groupsWithMembers.Add(full);
			}

			// GetAllAsync already includes each member's home group and
			// position, which is all the Members tab shows.
			var members = await _memberRepository.GetAllAsync(Token);

			MainThread.BeginInvokeOnMainThread(() =>
			{
				Groups.Clear();
				foreach (var g in groupsWithMembers)
					Groups.Add(new OverviewGroup(g, OnGroupToggledAsync));

				Positions.Clear();
				foreach (var p in positionsWithHolders)
					Positions.Add(new OverviewPosition(p, OnPositionToggledAsync));

				_allMembers = members
					.OrderBy(m => m.AnonymousName, StringComparer.CurrentCulture)
					.Select(m => new OverviewMember(m))
					.ToList();
				ApplyMemberFilter();

				UpdateSummaries();
			});
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to load registration overview");
			await _popupService.ShowErrorAsync(
				"Load Failed",
				"Could not load registrations. Pull to refresh and try again.");
		}
		finally
		{
			_suppressToggleHandlers = false;
			IsLoading = false;
			IsBusy = false;
		}
	}

	private void UpdateSummaries()
	{
		var groupRegistered = Groups.Count(g => g.IsRegistered);
		var positionRegistered = Positions.Count(p => p.IsRegistered);
		GroupsSummary = $"{groupRegistered} of {Groups.Count} registered";
		PositionsSummary = $"{positionRegistered} of {Positions.Count} registered";

		var inGroup = _allMembers.Count(m => m.Entity.HomeGroupId != null);
		var inPosition = _allMembers.Count(m => m.Entity.IntergroupPositionId != null);
		MembersSummary = $"{_allMembers.Count} members · {inGroup} in a group · {inPosition} in a position";
	}

	// =================================================================
	// Members tab
	// =================================================================

	partial void OnMemberSearchTextChanged(string value) => ApplyMemberFilter();

	/// <summary>
	/// Rebuilds <see cref="Members"/> from <see cref="_allMembers"/>,
	/// keeping rows whose name, group or position contains the search
	/// text. An empty search shows everyone.
	/// </summary>
	private void ApplyMemberFilter()
	{
		var term = MemberSearchText?.Trim();
		var matches = string.IsNullOrEmpty(term)
			? _allMembers
			: _allMembers.Where(m => m.Matches(term));

		Members.Clear();
		foreach (var m in matches)
			Members.Add(m);
	}

	[RelayCommand]
	private async Task MoveMember(OverviewMember row)
	{
		if (row is null) return;

		var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			{ "memberId", row.Entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
		};
		await Shell.Current.GoToAsync(nameof(MoveMemberPage), parameters);
	}

	// =================================================================
	// Toggle handlers — called by the row wrappers when their Switch changes
	// =================================================================

	private async Task OnGroupToggledAsync(OverviewGroup row, bool desired)
	{
		if (_suppressToggleHandlers) return;

		// IMPORTANT: compare `desired` against the *persisted* registration
		// state (Entity.Registered) — NOT row.IsRegistered. By the time
		// this handler runs the source-generated setter has already pushed
		// `desired` into the field, so row.IsRegistered == desired and
		// neither branch below would ever match.
		bool currentlyRegistered = row.Entity.Registered;

		// When registering, defer to the Verify flow so the GSR can be
		// chosen / confirmed. The Verify page performs the actual
		// Register() call and navigates back.
		if (desired && !currentlyRegistered)
		{
			// Flip the switch back to its previous state — Verify will
			// flip it forward on successful registration when we reload.
			row.RevertToggle();
			await NavigateToGroupVerify(row.Entity.Id);
			return;
		}

		if (!desired && currentlyRegistered)
		{
			bool confirmed = await Shell.Current.DisplayAlertAsync(
				"Unregister Group",
				$"Unregister {row.Entity.Name}?",
				"Yes", "No");

			if (!confirmed)
			{
				row.RevertToggle();
				return;
			}

			try
			{
				await _groupAttendance.Unregister(row.Entity);
				row.ApplyNewState(false);
				Logger.Information("Unregistered group {Name} from overview page", row.Entity.Name);
				UpdateSummaries();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to unregister group {Id} from overview", row.Entity.Id);
				row.RevertToggle();
				await _popupService.ShowErrorAsync("Unregister Failed", ex.Message);
			}
		}
	}

	private async Task OnPositionToggledAsync(OverviewPosition row, bool desired)
	{
		if (_suppressToggleHandlers) return;

		// See note in OnGroupToggledAsync — compare against the persisted
		// Entity.Registered, not the just-toggled row.IsRegistered.
		bool currentlyRegistered = row.Entity.Registered;

		if (desired && !currentlyRegistered)
		{
			row.RevertToggle();
			await NavigateToPositionVerify(row.Entity.Id);
			return;
		}

		if (!desired && currentlyRegistered)
		{
			bool confirmed = await Shell.Current.DisplayAlertAsync(
				"Unregister Officer",
				$"Unregister {row.Entity.ShortDescription}?",
				"Yes", "No");

			if (!confirmed)
			{
				row.RevertToggle();
				return;
			}

			try
			{
				await _positionAttendance.Unregister(row.Entity);
				row.ApplyNewState(false);
				Logger.Information("Unregistered position {Name} from overview page", row.Entity.ShortDescription);
				UpdateSummaries();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to unregister position {Id} from overview", row.Entity.Id);
				row.RevertToggle();
				await _popupService.ShowErrorAsync("Unregister Failed", ex.Message);
			}
		}
	}

	// =================================================================
	// Details / Edit navigation
	// =================================================================

	[RelayCommand]
	private async Task OpenGroupDetails(OverviewGroup row)
	{
		if (row is null) return;
		await NavigateToGroupVerify(row.Entity.Id);
	}

	[RelayCommand]
	private async Task OpenPositionDetails(OverviewPosition row)
	{
		if (row is null) return;
		await NavigateToPositionVerify(row.Entity.Id);
	}

	private static Task NavigateToGroupVerify(int groupId)
	{
		var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			{ "groupId", groupId.ToString() },
			{ "entrySource", "overview" }
		};
		return Shell.Current.GoToAsync(nameof(VerifyGroupPage), parameters);
	}

	private static Task NavigateToPositionVerify(int positionId)
	{
		var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			{ "positionId", positionId.ToString() },
			{ "entrySource", "overview" }
		};
		return Shell.Current.GoToAsync(nameof(VerifyPositionPage), parameters);
	}
}

// =====================================================================
// Row wrappers
//
// These are lightweight ObservableObjects that wrap a Group or Position
// and expose an IsRegistered property the Switch can bind to two-way.
// They forward toggle changes to the view-model's async handler via a
// captured delegate, and know how to revert themselves if the handler
// decides not to proceed (user cancel, error, or deferred to Verify).
// =====================================================================

public partial class OverviewGroup : ObservableObject
{
	public Group Entity { get; }

	private readonly Func<OverviewGroup, bool, Task> _onToggled;
	private bool _lastAppliedValue;

	public OverviewGroup(Group entity, Func<OverviewGroup, bool, Task> onToggled)
	{
		Entity = entity;
		_onToggled = onToggled;
		isRegistered = entity.Registered;
		_lastAppliedValue = entity.Registered;
	}

	[ObservableProperty]
	private bool isRegistered;

	partial void OnIsRegisteredChanged(bool value)
	{
		// Re-evaluate IsToggleEnabled — once unregistered, a row with no
		// GSR locks itself off; once registered, it can always be turned
		// off again.
		OnPropertyChanged(nameof(IsToggleEnabled));

		// If we're reverting the toggle programmatically, don't fire
		// the handler — only user-driven changes should trigger work.
		if (value == _lastAppliedValue) return;
		_lastAppliedValue = value;
		_ = _onToggled(this, value);
	}

	/// <summary>
	/// True when the group has either a confirmed GSR member or a named
	/// proxy. Registering with no representative present is meaningless,
	/// so the toggle is disabled in that case.
	/// </summary>
	public bool HasRepresentative =>
		(Entity.GsrProxy && !string.IsNullOrWhiteSpace(Entity.GsrProxyName))
		|| Entity.Members?.Any(m => m.IsGsr) == true;

	/// <summary>
	/// Switch is enabled when the group has a representative, or — for
	/// edge cases where a previously-registered group had its GSR
	/// removed elsewhere — when it's already registered (so the user
	/// can still turn it off).
	/// </summary>
	public bool IsToggleEnabled => HasRepresentative || IsRegistered;

	public string DisplayGsr
	{
		get
		{
			if (Entity.GsrProxy && !string.IsNullOrWhiteSpace(Entity.GsrProxyName))
				return $"Proxy: {Entity.GsrProxyName}";

			var gsr = Entity.Members?.FirstOrDefault(m => m.IsGsr);
			return gsr?.AnonymousName ?? "No GSR assigned";
		}
	}

	public void RevertToggle()
	{
		_lastAppliedValue = Entity.Registered;
		IsRegistered = Entity.Registered;
	}

	public void ApplyNewState(bool registered)
	{
		Entity.Registered = registered;
		_lastAppliedValue = registered;
		IsRegistered = registered;
	}
}

public partial class OverviewPosition : ObservableObject
{
	public Position Entity { get; }

	private readonly Func<OverviewPosition, bool, Task> _onToggled;
	private bool _lastAppliedValue;

	public OverviewPosition(Position entity, Func<OverviewPosition, bool, Task> onToggled)
	{
		Entity = entity;
		_onToggled = onToggled;
		isRegistered = entity.Registered;
		_lastAppliedValue = entity.Registered;
	}

	[ObservableProperty]
	private bool isRegistered;

	partial void OnIsRegisteredChanged(bool value)
	{
		OnPropertyChanged(nameof(IsToggleEnabled));

		if (value == _lastAppliedValue) return;
		_lastAppliedValue = value;
		_ = _onToggled(this, value);
	}

	/// <summary>
	/// True when the position has at least one assigned holder.
	/// Registering an empty position is meaningless, so the toggle is
	/// disabled in that case.
	/// </summary>
	public bool HasHolder => Entity.Holders?.Any() == true;

	/// <summary>
	/// Switch is enabled when the position has a holder, or — for the
	/// edge case where it's already registered but the holder was
	/// removed elsewhere — when it's already registered (so the user
	/// can still turn it off).
	/// </summary>
	public bool IsToggleEnabled => HasHolder || IsRegistered;

	public string DisplayHolder
	{
		get
		{
			var holder = Entity.Holders?.FirstOrDefault();
			return holder?.AnonymousName ?? "Vacant";
		}
	}

	public void RevertToggle()
	{
		_lastAppliedValue = Entity.Registered;
		IsRegistered = Entity.Registered;
	}

	public void ApplyNewState(bool registered)
	{
		Entity.Registered = registered;
		_lastAppliedValue = registered;
		IsRegistered = registered;
	}
}

public enum OverviewTab
{
	Groups,
	Positions,
	Members,
}

/// <summary>
/// Read-only row for the Members tab: a member and the group and position
/// they are currently assigned to. Moving happens on
/// <see cref="MoveMemberPage"/>, and the list reloads on return, so a row
/// never changes in place.
/// </summary>
public sealed class OverviewMember
{
	public OverviewMember(Member entity) => Entity = entity;

	public Member Entity { get; }

	public bool HasGroup => Entity.HomeGroup is not null;

	public bool HasPosition => Entity.IntergroupPosition is not null;

	public string DisplayGroup
	{
		get
		{
			if (Entity.HomeGroup is null) return "No group";
			return Entity.IsGsr ? $"{Entity.HomeGroup.Name} · GSR" : Entity.HomeGroup.Name;
		}
	}

	public string DisplayPosition
	{
		get
		{
			if (Entity.IntergroupPosition is null) return "No position";
			var name = Entity.IntergroupPosition.ShortDescription;
			return string.IsNullOrWhiteSpace(Entity.IntergroupPositionRotation)
				? name
				: $"{name} · rotates {Entity.IntergroupPositionRotation}";
		}
	}

	public bool Matches(string term) =>
		Entity.AnonymousName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
		|| (Entity.HomeGroup?.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
		|| (Entity.IntergroupPosition?.ShortDescription.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false);
}