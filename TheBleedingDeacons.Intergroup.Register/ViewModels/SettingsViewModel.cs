using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<SettingsViewModel>();

        private readonly IConfigurationService _configService;

        public SettingsViewModel(IConfigurationService configService)
        {
            _configService = configService;
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

        private async Task ShowFeedback()
        {
            await Task.Delay(100);
        }
    }
}