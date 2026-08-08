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
/// </summary>
public partial class RegistrationOverviewViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<RegistrationOverviewViewModel>();

	private readonly IGroupRepository _groupRepository;
	private readonly IPositionRepository _positionRepository;
	private readonly IAttendanceRegistration<Group> _groupAttendance;
	private readonly IAttendanceRegistration<Position> _positionAttendance;
	private readonly IPopupNotification _popupService;

	/// <summary>
	/// Flag used to suppress the change-handlers on OverviewGroup.IsRegistered /
	/// OverviewPosition.IsRegistered while we're rebuilding the lists from the
	/// database. Without this guard the act of refreshing the page would
	/// re-register everything. Set inside LoadAsync, reset on exit.
	/// </summary>
	private bool _suppressToggleHandlers;

	public ObservableCollection<OverviewGroup> Groups { get; } = new();
	public ObservableCollection<OverviewPosition> Positions { get; } = new();

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsPositionsTabSelected))]
	private bool isGroupsTabSelected = true;

	public bool IsPositionsTabSelected => !IsGroupsTabSelected;

	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private string groupsSummary = string.Empty;

	[ObservableProperty]
	private string positionsSummary = string.Empty;

	public RegistrationOverviewViewModel(
		IGroupRepository groupRepository,
		IPositionRepository positionRepository,
		IAttendanceRegistration<Group> groupAttendance,
		IAttendanceRegistration<Position> positionAttendance,
		IPopupNotification popupService)
	{
		_groupRepository = groupRepository;
		_positionRepository = positionRepository;
		_groupAttendance = groupAttendance;
		_positionAttendance = positionAttendance;
		_popupService = popupService;
		Title = "Registrations";
	}

	// =================================================================
	// Tab switching
	// =================================================================

	[RelayCommand]
	private void ShowGroupsTab() => IsGroupsTabSelected = true;

	[RelayCommand]
	private void ShowPositionsTab() => IsGroupsTabSelected = false;

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

			MainThread.BeginInvokeOnMainThread(() =>
			{
				Groups.Clear();
				foreach (var g in groupsWithMembers)
					Groups.Add(new OverviewGroup(g, OnGroupToggledAsync));

				Positions.Clear();
				foreach (var p in positionsWithHolders)
					Positions.Add(new OverviewPosition(p, OnPositionToggledAsync));

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