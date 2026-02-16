using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class GroupSelectionViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<GroupSelectionViewModel>();

    private readonly IGroupRepository _groupRepository;

    public ObservableCollection<Group> Groups { get; } = new();

    [ObservableProperty]
    GroupCriteria criteria;

    [ObservableProperty]
    string header = string.Empty;

    [ObservableProperty]
    bool isLoading = false;

    [ObservableProperty]
    bool isDataLoaded = false;


    public GroupSelectionViewModel(IGroupRepository groupRepository)
    {

        _groupRepository = groupRepository;

        Title = "Select Meeting";
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {

        if (query == null) return;

        Criteria = (GroupCriteria)query["criteria"];


    }

    partial void OnCriteriaChanged(GroupCriteria value)
    {
        // Use MainThread for proper Android compatibility
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadDataAsync();
        });
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            IsLoading = true;
            IsDataLoaded = false;


            await Task.Yield();

            Groups.Clear();

            var allGroups = await _groupRepository.GetAllGroupsAsync();

            var filteredGroups = allGroups
                .Where(g =>
                {
                    return string.Equals(g.Day, Criteria.Day, StringComparison.OrdinalIgnoreCase) && g.IsOnline() == (Criteria.MeetingType == "Online");
                }).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {

                foreach (var group in filteredGroups)
                {
                    Groups.Add(group);
                }

                IsDataLoaded = true;
                IsLoading = false;
            });


            //HasGroups = Groups.Count > 0;

            Header = $"{Criteria.Day} {Criteria.MeetingType} Meetings";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    async Task SelectGroup(Group group)
    {
        if (group == null) return;

        //await ShowFeedback();

        var parameters = new Dictionary<string, object> {
                {"groupId", group.ID.ToString()} };

        //await Shell.Current.GoToAsync(nameof(GroupEditPage), parameters);
        await Shell.Current.GoToAsync(nameof(GsrVerifyPage), parameters);
    }

    //private async Task ShowFeedback()
    //{
    //    await Task.Delay(100);
    //    await Toast.Make("Loading Group...", ToastDuration.Short).Show();
    //}

}