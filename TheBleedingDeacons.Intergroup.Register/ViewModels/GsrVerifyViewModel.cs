using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

[QueryProperty(nameof(GroupId), "groupId")]
[QueryProperty(nameof(Edited), "edited")]
public partial class GsrVerifyViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<GsrVerifyViewModel>();

    private readonly DataService _dataService;
    private readonly IAttendanceRegistration<Group> _attendanceRegistration;
    private readonly IGroupRepository _groupRepository;
    private readonly IPopupNotification _popupService;

    [ObservableProperty]
    private string attendedStatusText = string.Empty;

    [ObservableProperty]
    private int groupId;

    // Initialize Group to avoid null reference exceptions in bindings
    [ObservableProperty]
    private Group group = new();

    [ObservableProperty]
    private bool edited;

    [ObservableProperty]
    private bool standingIn = false;

    [ObservableProperty]
    private string? standinEmail;

    [ObservableProperty]
    private string? standinName;

    [ObservableProperty]
    private bool canRegister = false;

    [ObservableProperty]
    private bool isLoading;

    public GsrVerifyViewModel(DataService dataService, IAttendanceRegistration<Group> attendanceRegistration, IGroupRepository groupRepository, IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _dataService = dataService;
        _groupRepository = groupRepository;
        _popupService = popupService;
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("ApplyQueryAttributes called with {Count} parameters", query.Count);

        foreach (var kvp in query)
        {
            Logger.Information("  Query param: {Key} = {Value} (Type: {Type})",
                kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
        }

        // Handle edited flag
        if (query.TryGetValue("edited", out var editedObj) &&
            editedObj?.ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Group));
                // Null-safe check for HasAll
                CanRegister = Group?.HasAll() ?? false;
            });
        }

        // Handle groupId passed as string from navigation
        if (query.TryGetValue("groupId", out var groupIdObj))
        {
            int parsedGroupId = 0;

            if (groupIdObj is string groupIdStr)
            {
                Logger.Information("Parsing groupId from string: {GroupIdStr}", groupIdStr);
                int.TryParse(groupIdStr, out parsedGroupId);
            }
            else if (groupIdObj is int intValue)
            {
                Logger.Information("GroupId is already int: {IntValue}", intValue);
                parsedGroupId = intValue;
            }

            Logger.Information("Parsed groupId: {ParsedGroupId}", parsedGroupId);

            if (parsedGroupId > 0)
            {
                GroupId = parsedGroupId;
            }
        }
    }

    partial void OnGroupIdChanged(int value)
    {
        Logger.Information("OnGroupIdChanged triggered with value: {Value}", value);

        if (value > 0)
        {
            // Use MainThread.BeginInvokeOnMainThread for the async call
            // This ensures proper context on Android
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadGroupAsync(value);
            });
        }
    }

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

        await Shell.Current.GoToAsync(nameof(GsrEditPage), parameters);
    }

    [RelayCommand]
    public async Task Yes()
    {
        if (Group == null)
        {
            Logger.Warning("Cannot register - Group is null");
            return;
        }

        try
        {
            Group.ProxyAttendance = StandingIn;
            Group.ProxyEmail = StandinEmail;
            Group.ProxyName = StandinName;

            await _attendanceRegistration.Register(Group);

            string personalName = Group.GetFirstName();

            // Show success popup
            await _popupService.ShowCountdownPopupAsync(
                "Finished",
                $"Thanks for registering {personalName}.",
                async () => await Shell.Current.GoToAsync("//MainPage")
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to register attendance");
            await Application.Current?.MainPage?.DisplayAlert("Error", $"Failed to register: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    public async Task Cancel()
    {
        await Shell.Current.GoToAsync("///MainPage");
    }

    private async Task LoadGroupAsync(int groupId)
    {
        Logger.Information("LoadGroupAsync called with groupId: {GroupId}", groupId);

        if (IsLoading) return;

        try
        {
            IsLoading = true;

            var loadedGroup = await _groupRepository.GetGroupDirectlyAsync(groupId);

            Logger.Information("Group loaded: {GroupName}, GSR: {GsrName}",
                loadedGroup?.Name ?? "null",
                loadedGroup?.GsrName ?? "null");

            if (loadedGroup != null)
            {
                // All UI updates happen here on the main thread
                Group = loadedGroup;

                // Set title with null-safe checks
                if (!string.IsNullOrEmpty(loadedGroup.Name) &&
                    !string.IsNullOrEmpty(loadedGroup.Day) &&
                    !loadedGroup.Name.Contains(loadedGroup.Day))
                {
                    Title = $"{loadedGroup.Name} on {loadedGroup.Day}";
                }
                else
                {
                    Title = loadedGroup.Name ?? "Unknown Group";
                }

                CanRegister = loadedGroup.HasAll();

                // Force property change notification for bindings
                OnPropertyChanged(nameof(Group));

                Logger.Information("UI updated - Group: {GroupName}, CanRegister: {CanRegister}",
                    Group?.Name, CanRegister);
            }
            else
            {
                Logger.Warning("Group not found for ID: {GroupId}", groupId);
                await Application.Current?.MainPage?.DisplayAlert(
                    "Not Found",
                    $"Group with ID {groupId} was not found.",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load group {GroupId}", groupId);

            try
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "Error",
                    $"Failed to load group: {ex.Message}",
                    "OK");
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
}