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

public partial class MeetingSelectionViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MeetingSelectionViewModel>();

    private readonly IMeetingRepository _meetingRepository;

    public ObservableCollection<Meeting> Meetings { get; } = new();

    [ObservableProperty]
    MeetingCriteria criteria;

    [ObservableProperty]
    string header = string.Empty;

    [ObservableProperty]
    bool isLoading = false;

    [ObservableProperty]
    bool isDataLoaded = false;


    public MeetingSelectionViewModel(IMeetingRepository meetingRepository)
    {

        _meetingRepository = meetingRepository;

        Title = "Select Meeting";
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {

        if (query == null) return;

        Criteria = (MeetingCriteria)query["criteria"];


    }

    partial void OnCriteriaChanged(MeetingCriteria value)
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

            Meetings.Clear();

            var allMeetings = await _meetingRepository.GetAllMeetingsAsync();

            var filteredMeetings = allMeetings
                .Where(m =>
                {
                    return string.Equals(m.Day, Criteria.Day, StringComparison.OrdinalIgnoreCase) && m.IsOnline() == (Criteria.MeetingType == "Online");
                }).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {

                foreach (var meeting in filteredMeetings)
                {
                    Meetings.Add(meeting);
                }

                IsDataLoaded = true;
                IsLoading = false;
            });


            Header = $"{Criteria.Day} {Criteria.MeetingType} Meetings";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    async Task SelectMeeting(Meeting meeting)
    {
        if (meeting == null) return;

        var parameters = new Dictionary<string, object> {
                {"groupId", meeting.ID.ToString()} };

        await Shell.Current.GoToAsync(nameof(GroupVerifyPage), parameters);
    }

}
