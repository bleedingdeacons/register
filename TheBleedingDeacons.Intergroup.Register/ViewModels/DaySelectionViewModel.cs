using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class DaySelectionViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<DaySelectionViewModel>();

    [ObservableProperty]
    private ObservableCollection<DayItem> days;

    [ObservableProperty]
    private DayItem? selectedDay;

    public DaySelectionViewModel()
    {

        Title = "Select a Day";

        days = new ObservableCollection<DayItem>
        {
            new("Monday"),
            new("Tuesday"),
            new("Wednesday"),
            new("Thursday"),
            new("Friday"),
            new("Saturday"),
            new("Sunday")
        };
    }

    [RelayCommand]
    async Task SelectDay(DayItem day)
    {

        var parameters = new Dictionary<string, object>{

                {"day", day.Name}};

        await Shell.Current.GoToAsync(nameof(TypeSelectionPage), parameters);


    }

}