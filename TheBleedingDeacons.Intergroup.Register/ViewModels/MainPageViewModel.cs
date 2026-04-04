using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class MainPageViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<MainPageViewModel>();

    private const string BaseTitle = "Intergroup Registration";

    private readonly IConfigurationService _configService;
    private readonly IIntergroupMeetingRepository _intergroupMeetingRepository;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsButtonsEnabled))]
    private bool isMeetingSelected = false;

    [ObservableProperty]
    private string activeMeetingDate = string.Empty;

    public bool IsButtonsEnabled => IsMeetingSelected && !IsBusy;

    public string AppVersion => $"v{AppInfo.VersionString}";

    public MainPageViewModel(IConfigurationService configService, IIntergroupMeetingRepository intergroupMeetingRepository)
    {
        _configService = configService;
        _intergroupMeetingRepository = intergroupMeetingRepository;
        Title = BaseTitle;
    }

    // Called each time the page appears so meeting state refreshes.
    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        base.ApplyQueryAttributes(query);
    }

    public async Task RefreshMeetingStateAsync()
    {
        var config = await _configService.LoadUnityConfigurationAsync();

        if (config.ActiveIntergroupMeetingId.HasValue)
        {
            // Verify the meeting still exists in the database
            var meeting = await _intergroupMeetingRepository
                .GetByIdAsync(config.ActiveIntergroupMeetingId.Value);

            if (meeting != null)
            {
                IsMeetingSelected = true;
            }
            else
            {
                // Meeting ID in SecureStorage is stale (DB was cleared) — clean up
                await _configService.SaveActiveIntergroupMeetingAsync(null);
                IsMeetingSelected = false;
                Logger.Information("Cleared stale active meeting ID {Id} — meeting no longer in database",
                    config.ActiveIntergroupMeetingId.Value);
            }
        }
        else
        {
            IsMeetingSelected = false;
        }

        OnPropertyChanged(nameof(IsButtonsEnabled));
    }

    [RelayCommand]
    async Task GoToAdmin()
    {
        await Shell.Current.GoToAsync("//AdminPage");
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