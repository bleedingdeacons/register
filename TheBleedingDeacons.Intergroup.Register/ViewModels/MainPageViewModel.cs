using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class MainPageViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MainPageViewModel>();

    public MainPageViewModel()
    {
        Title = "Intergroup Registration";
    }

    [RelayCommand]
    async Task SelectDay()
    {
        await Shell.Current.GoToAsync(nameof(DaySelectionPage));
    }

    [RelayCommand]
    async Task SelectPosition()
    {
        await Shell.Current.GoToAsync(nameof(PositionSelectionPage));
    }

    
}