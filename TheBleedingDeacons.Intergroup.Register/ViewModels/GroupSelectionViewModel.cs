using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class GroupSelectionViewModel : BaseViewModel
{
    private readonly IGroupRepository _groupRepository;
    private readonly IMeetingRepository _meetingRepository;

    public ObservableCollection<Group> Groups { get; } = new();

    [ObservableProperty]
    private MeetingCriteria criteria;

    [ObservableProperty]
    private string header = string.Empty;

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool isDataLoaded = false;

    public GroupSelectionViewModel(
        IGroupRepository groupRepository,
        IMeetingRepository meetingRepository)
    {
        _groupRepository = groupRepository;
        _meetingRepository = meetingRepository;
        Title = "Select Group";
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query == null) return;
        Criteria = (MeetingCriteria)query["criteria"];
    }

    partial void OnCriteriaChanged(MeetingCriteria value)
    {
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

            // Get all meetings matching the day/type criteria
            var allMeetings = await _meetingRepository.GetAllAsync();

            var filteredMeetings = allMeetings
                .Where(m =>
                {
                    return string.Equals(m.DayOfWeek, Criteria.Day, StringComparison.OrdinalIgnoreCase)
                        && m.IsOnline() == (Criteria.MeetingType == "Online");
                }).ToList();

            // Get the distinct groups from those meetings
            var groupIds = filteredMeetings
                .Where(m => m.GroupId.HasValue && m.GroupId.Value > 0)
                .Select(m => m.GroupId!.Value)
                .Distinct()
                .ToList();

            var groups = new List<Group>();
            foreach (var groupId in groupIds)
            {
                var group = await _groupRepository.GetByIdWithMembersAsync(groupId);
                if (group != null)
                    groups.Add(group);
            }

            // Sort by group name
            groups = groups.OrderBy(g => g.Name, StringComparer.CurrentCulture).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var group in groups)
                {
                    Groups.Add(group);
                }

                IsDataLoaded = true;
                IsLoading = false;
            });

            Header = $"{Criteria.Day} {Criteria.MeetingType} Groups";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectGroup(Group group)
    {
        if (group == null) return;

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) {
            {"groupId", group.Id.ToString()} };

        await Shell.Current.GoToAsync(nameof(VerifyGroupPage), parameters);
    }
}
