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
    private readonly IConfigurationService _configService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsButtonsEnabled))]
    private bool isMeetingSelected = false;

    [ObservableProperty]
    private string activeMeetingDate = string.Empty;

    public bool IsButtonsEnabled => IsMeetingSelected && !IsBusy;

    public MainPageViewModel(IApiQueueService apiQueueService, IConfigurationService configService)
    {
        _apiQueueService = apiQueueService;
        _configService = configService;
        UpdateTitle();

        // Keep the title in sync whenever offline mode is toggled from Settings
        _apiQueueService.PendingCountChanged += (_, _) => UpdateTitle();
    }

    // Called each time the page appears so title and meeting state refresh.
    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        base.ApplyQueryAttributes(query);
        UpdateTitle();
    }

    public async Task RefreshMeetingStateAsync()
    {
        var config = await _configService.LoadUnityConfigurationAsync();
        IsMeetingSelected = config.ActiveIntergroupMeetingId.HasValue;
        OnPropertyChanged(nameof(IsButtonsEnabled));
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
