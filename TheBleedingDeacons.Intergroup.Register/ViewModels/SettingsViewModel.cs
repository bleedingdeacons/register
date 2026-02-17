using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<SettingsViewModel>();

        [RelayCommand]
        private async Task NavigateToMailSetting()
       {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(MailSettingsPage));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task NavigateToUnitySetting()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(UnitySettingsPage));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task NavigateToImportExport()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(ImportExportPage));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task NavigateToBackup()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(DatabaseBackupPage));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task NavigateToMailStatus()
        {
            await ShowFeedback();
            await Shell.Current.GoToAsync(nameof(EmailStatusPage));
            await Task.CompletedTask;
        }

        //[RelayCommand]
        //private async Task NavigateToAbout()
        //{
        //    // TODO: Add navigation to About page
        //    // Example: await Shell.Current.GoToAsync(nameof(AboutPage));
        //    await Task.CompletedTask;
        //}

        private async Task ShowFeedback()
        {
            await Task.Delay(100);
            //await Toast.Make("Loading...", ToastDuration.Short).Show();
        }
    }
}
