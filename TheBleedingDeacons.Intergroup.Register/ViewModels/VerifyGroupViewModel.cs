using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
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
	private readonly IPopupNotification _popupService;

	[ObservableProperty]
	private Group? group;

	[ObservableProperty]
	private int groupId;

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
	private bool canRegister;

	[ObservableProperty]
	private bool isLoading;

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
		IPopupNotification popupService)
	{
		_attendanceRegistration = attendanceRegistration;
		_groupRepository = groupRepository;
		_popupService = popupService;
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
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				if (GroupId > 0)
					await LoadGroupAsync(GroupId);
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

		await ShowFeedback();

		await Shell.Current.GoToAsync(nameof(GroupEditPage), parameters);
	}

	/// <summary>
	/// User confirms details are correct — register attendance for the group.
	/// </summary>
	[RelayCommand]
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
			// Set proxy state on entity so AttendanceService persists it
			Group.GsrProxy = StandingIn;
			Group.GsrProxyName = StandingIn ? StandinName : null;

			await _attendanceRegistration.Register(Group);

			await _popupService.ShowCountdownPopupAsync(
				"Complete",
				$"Thanks {Group.Name}",
				async () => await Shell.Current.GoToAsync("//MainPage")
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
		CanRegister = ActiveGsrs.Any(g =>
			!string.IsNullOrEmpty(g.AnonymousName) &&
			(!string.IsNullOrEmpty(g.MobileNumber) || !string.IsNullOrEmpty(g.PersonalEmail)));
	}

	#endregion
}