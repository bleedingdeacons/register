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

	private const string BaseTitle = "Intergroup Attendance Register";

	private readonly IConfigurationService _configService;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsButtonsEnabled))]
	private bool isMeetingSelected = false;

	[ObservableProperty]
	private string activeMeetingDate = string.Empty;

	public bool IsButtonsEnabled => IsMeetingSelected && !IsBusy;

	[ObservableProperty]
	public string appVersion;

	public MainPageViewModel(IConfigurationService configService)
	{
		_configService = configService;
		Title = BaseTitle;

		AppVersion = MauiProgram.AppVersion();
	}

	// Called each time the page appears so meeting state refreshes.
	public override void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		base.ApplyQueryAttributes(query);
	}

	public async Task RefreshMeetingStateAsync()
	{
		var config = await _configService.LoadUnityConfigurationAsync();
		IsMeetingSelected = config.ActiveIntergroupMeetingId.HasValue;
		OnPropertyChanged(nameof(IsButtonsEnabled));
	}

	[RelayCommand]
	async Task GoToAdmin()
	{
		await Shell.Current.GoToAsync("//AdminPage");
	}

	[RelayCommand]
	async Task SelectType()
	{
		await Shell.Current.GoToAsync(nameof(TypeSelectionPage));
	}

	[RelayCommand]
	async Task SelectPosition()
	{
		await Shell.Current.GoToAsync(nameof(PositionSelectionPage));
	}

}