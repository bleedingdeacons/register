using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class MainPageViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MainPageViewModel>();

    private const string BaseTitle = "Intergroup Registration";

    private readonly IApiQueueService _apiQueueService;

    public MainPageViewModel(IApiQueueService apiQueueService)
    {
        _apiQueueService = apiQueueService;
        UpdateTitle();

        // Keep the title in sync whenever offline mode is toggled from Settings
        _apiQueueService.PendingCountChanged += (_, _) => UpdateTitle();
    }

    // Called each time the page appears so the title refreshes if the user
    // changed the offline mode switch on the Settings page and came back.
    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        base.ApplyQueryAttributes(query);
        UpdateTitle();
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

    // ------------------------------------------------------------------ private

    private void UpdateTitle()
    {
        Title = _apiQueueService.IsOfflineModeEnabled
            ? $"{BaseTitle} (Offline)"
            : BaseTitle;
    }
}