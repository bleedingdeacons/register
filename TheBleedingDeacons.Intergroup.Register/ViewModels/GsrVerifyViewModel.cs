using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Serilog;
using System.Diagnostics;
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

    private readonly DataService _registrationService;
    private readonly IAttendanceRegistration<Group> _attendanceRegistration;
    private readonly IGroupRepository _groupRepository;
    private readonly IPopupNotification _popupService;

    [ObservableProperty]
    private string attendedStatusText = string.Empty;

    [ObservableProperty]
    private int groupId;

    [ObservableProperty]
    private Group group;

    [ObservableProperty]
    private bool edited;

    [ObservableProperty]
    private bool canRegister = false;

    public GsrVerifyViewModel(DataService registrationService, IAttendanceRegistration<Group> attendanceRegistration, IGroupRepository groupRepository, IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _registrationService = registrationService;
        _groupRepository = groupRepository;
        _popupService = popupService;
    }

    [ObservableProperty]
    private bool isLoading;

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.ContainsKey("edited") &&
            query["edited"].ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Group));
                CanRegister = Group.HasAll();
            });
        }
    }


    partial void OnGroupIdChanged(int value)
    {
        Task.Run(async () => await LoadGroup(value));
    }

    [RelayCommand]
    public async Task No()
    {

        var parameters = new Dictionary<string, object>
        {
            ["group"] = Group
        };

        await Shell.Current.GoToAsync(nameof(GsrEditPage), parameters);


    }

    [RelayCommand]
    public async Task Yes()
    {

        await _attendanceRegistration.Register(Group);

        string personalName = Group.GetGsrFirstName();

        // Show success popup
        await _popupService.ShowCountdownPopupAsync(
            "Finished",
            $"Thanks for registering {personalName}.",
            async () => await Shell.Current.GoToAsync("//MainPage")
        );
    }

    [RelayCommand]
    public async Task Cancel()
    {
        await Shell.Current.GoToAsync("///MainPage");    
    }

    public async Task LoadGroup(int groupId)
    {
        try
        {
            IsLoading = true;
            var group = await _groupRepository.GetGroupDirectlyAsync(groupId);
            if (group != null)
            {
                Group = group;
                if (!group.Name.Contains(group.Day))
                    Title = $"{group.Name} on {group.Day}";
                else
                    Title = $"{group.Name}";

                CanRegister = group.HasAll();
            }
        }
        catch (Exception ex)
        {
            // Handle error - you might want to show an alert or log this
            await Application.Current.MainPage.DisplayAlert("Error", $"Failed to load group: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }



}