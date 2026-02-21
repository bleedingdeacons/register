using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<SettingsViewModel>();

        private readonly DataService _dataService;
        private readonly IUnityApiService _unityApiService;
        private readonly IConfigurationService _configService;
        private readonly IApiQueueService _apiQueueService;

        public SettingsViewModel(
            DataService dataService,
            IUnityApiService unityApiService,
            IConfigurationService configService,
            IApiQueueService apiQueueService)
        {
            _dataService = dataService;
            _unityApiService = unityApiService;
            _configService = configService;
            _apiQueueService = apiQueueService;
        }

        [ObservableProperty]
        private bool isSyncing = false;

        [ObservableProperty]
        private string syncStatusMessage = string.Empty;

        [ObservableProperty]
        private bool isSyncStatusVisible = false;

        [ObservableProperty]
        private bool isSyncStatusError = false;

        // ------------------------------------------------------------------ offline mode

        public bool IsOfflineModeEnabled
        {
            get => _apiQueueService.IsOfflineModeEnabled;
            set
            {
                if (_apiQueueService.IsOfflineModeEnabled == value) return;
                _apiQueueService.IsOfflineModeEnabled = value;
                OnPropertyChanged();
                Logger.Information("Offline mode toggled {State}", value ? "ON" : "OFF");
            }
        }

        // ------------------------------------------------------------------ sync

        [RelayCommand]
        private async Task SyncFromUnity()
        {
            if (IsSyncing) return;

            try
            {
                IsSyncing = true;
                HideSyncStatus();

                var config = await _configService.LoadUnityConfigurationAsync();
                if (!config.IsValid())
                {
                    ShowSyncStatus("Unity API not configured. Go to Unity API Settings first.", true);
                    return;
                }

                ShowSyncStatus("Syncing from Unity API...", false);

                var (meetings, positions) = await _dataService.ImportFromUnityAsync(_unityApiService);

                ShowSyncStatus($"Imported {meetings} meetings and {positions} positions.", false);
                Logger.Information("Unity sync complete: {Meetings} meetings, {Positions} positions", meetings, positions);
            }
            catch (Exception ex)
            {
                ShowSyncStatus($"Sync failed: {ex.Message}", true);
                Logger.Error(ex, "Unity sync failed");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        [RelayCommand]
        private async Task NavigateToMailSetting()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(MailSettingsPage));
        }

        [RelayCommand]
        private async Task NavigateToUnitySetting()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(UnitySettingsPage));
        }

        [RelayCommand]
        private async Task NavigateToImportExport()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(ImportExportPage));
        }

        [RelayCommand]
        private async Task NavigateToBackup()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(DatabaseBackupPage));
        }

        [RelayCommand]
        private async Task NavigateToMailStatus()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(EmailStatusPage));
        }

        private void ShowSyncStatus(string message, bool isError)
        {
            SyncStatusMessage = isError ? $"\u274c {message}" : $"\u2705 {message}";
            IsSyncStatusError = isError;
            IsSyncStatusVisible = true;

            if (!isError && !message.Contains("Syncing"))
            {
                Task.Delay(5000).ContinueWith(_ =>
                {
                    IsSyncStatusVisible = false;
                    SyncStatusMessage = string.Empty;
                });
            }
        }

        private void HideSyncStatus()
        {
            IsSyncStatusVisible = false;
            SyncStatusMessage = string.Empty;
        }

        private async Task ShowFeedback()
        {
            await Task.Delay(100);
        }
    }
}