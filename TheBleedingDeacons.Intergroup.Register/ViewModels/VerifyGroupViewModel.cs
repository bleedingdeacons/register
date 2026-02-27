using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the read-only verification and registration flow for a meeting group.
/// The user confirms their GSR details are correct (Yes) or navigates to edit them (No).
///
/// Displays ALL active GSRs for the group as a member-centric list, filtering out
/// any members that have been marked for deletion.
///
/// Receives a groupId from navigation and loads the Group (with Meetings + GSRs)
/// directly, so verify/edit always operate on the group rather than a specific meeting.
/// </summary>
[QueryProperty(nameof(GroupId), "groupId")]
[QueryProperty(nameof(Edited), "edited")]
public partial class VerifyGroupViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<VerifyGroupViewModel>();

    private readonly IAttendanceRegistration<Meeting> _attendanceRegistration;
    private readonly RegisterContext _context;
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
    /// Active (non-deleted) GSR members for the group, displayed as a list.
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
        IAttendanceRegistration<Meeting> attendanceRegistration,
        RegisterContext context,
        IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _context = context;
        _popupService = popupService;
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("VerifyGroupViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        // Handle edited flag returning from Edit flow — reload from DB so updated
        // GSR values are reflected rather than the stale in-memory Group object.
        if (query.TryGetValue("edited", out var editedObj) &&
            editedObj?.ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (GroupId > 0)
                    await LoadGroupAsync(GroupId);
            });
        }

        // Handle groupId passed from navigation
        if (query.TryGetValue("groupId", out var groupIdObj))
        {
            int parsedGroupId = 0;

            if (groupIdObj is string groupIdStr)
                int.TryParse(groupIdStr, out parsedGroupId);
            else if (groupIdObj is int intValue)
                parsedGroupId = intValue;

            if (parsedGroupId > 0)
                GroupId = parsedGroupId;
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnGroupIdChanged(int value)
    {
        Logger.Information("OnGroupIdChanged triggered with value: {Value}", value);

        if (value > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadGroupAsync(value);
            });
        }
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

        await Shell.Current.GoToAsync(nameof(GroupEditPage), parameters);
    }

    /// <summary>
    /// User confirms details are correct — register attendance using the group's first meeting.
    /// </summary>
    [RelayCommand]
    public async Task Yes()
    {
        if (Group == null)
        {
            Logger.Warning("Cannot register - Group is null");
            return;
        }

        var meeting = Group.Meetings.FirstOrDefault();
        if (meeting == null)
        {
            Logger.Warning("Cannot register - Group {GroupId} has no meetings", Group.ID);
            var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (mainPage != null)
                await mainPage.DisplayAlert("Error", "No meeting found for this group.", "OK");
            return;
        }

        try
        {
            meeting.ProxyAttendance = StandingIn;
            meeting.ProxyEmail = StandinEmail;
            meeting.ProxyName = StandinName;

            await _attendanceRegistration.Register(meeting);

            string personalName = meeting.GetFirstName();

            await _popupService.ShowCountdownPopupAsync(
                "Finished",
                $"Thanks for registering {personalName}.",
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

            var loadedGroup = await _context.Groups
                .Include(g => g.Meetings)
                .Include(g => g.Gsrs)
                .FirstOrDefaultAsync(g => g.ID == groupId);

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
    /// Rebuild the observable list of active (non-deleted) GSRs from the loaded Group.
    /// </summary>
    private void RefreshActiveGsrs()
    {
        ActiveGsrs.Clear();

        if (Group?.Gsrs != null)
        {
            foreach (var gsr in Group.Gsrs.Where(g => !g.IsMarkedForDeletion))
            {
                ActiveGsrs.Add(gsr);
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
        // At least one active GSR must have all required contact fields filled in
        CanRegister = ActiveGsrs.Any(g =>
            !string.IsNullOrEmpty(g.Name) &&
            !string.IsNullOrEmpty(g.Phone) &&
            !string.IsNullOrEmpty(g.EmailPersonal));
    }

    #endregion
}
