using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Data;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<SettingsViewModel>();

        private readonly IConfigurationService _configService;
        private readonly UnityDbContext _dbContext;

        [ObservableProperty]
        private bool isPurging;

        [ObservableProperty]
        private string purgeStatusMessage = string.Empty;

        [ObservableProperty]
        private bool isPurgeStatusVisible;

        [ObservableProperty]
        private bool isPurgeStatusError;

        public SettingsViewModel(
            IConfigurationService configService,
            UnityDbContext dbContext)
        {
            _configService = configService;
            _dbContext = dbContext;
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

        /// <summary>
        /// Purges all data from the local database.
        /// Reuses the same <see cref="UnityDbContext.PurgeDatabaseAsync"/> method as AdminViewModel.
        /// </summary>
        [RelayCommand]
        private async Task PurgeDatabase()
        {
            bool confirmed = await Shell.Current.DisplayAlert(
                "Purge Database",
                "This will permanently delete ALL local data including groups, members, meetings, positions, and snapshots.\n\nThis action cannot be undone. Are you sure?",
                "Yes, Purge Everything",
                "No, Keep Data");

            if (!confirmed) return;

            try
            {
                IsPurging = true;
                ShowPurgeStatus("Purging database...", false);

                await _dbContext.PurgeDatabaseAsync();

                ShowPurgeStatus("Database purged successfully.", false);
                Logger.Information("Database purged successfully from Settings");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Database purge failed from Settings");
                ShowPurgeStatus($"Purge failed: {ex.Message}", true);
            }
            finally
            {
                IsPurging = false;
            }
        }

        private void ShowPurgeStatus(string message, bool isError)
        {
            PurgeStatusMessage = message;
            IsPurgeStatusError = isError;
            IsPurgeStatusVisible = true;
        }

        private async Task ShowFeedback()
        {
            await Task.Delay(100);
        }
    }
}
