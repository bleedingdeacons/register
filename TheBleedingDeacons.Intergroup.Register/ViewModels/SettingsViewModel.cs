using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services;
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
		private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
		private readonly RegistrationEventLog _eventLog;
		private readonly IBetterStackLoggerController _betterStackController;

		[ObservableProperty]
		private bool isPurging;

		[ObservableProperty]
		private string purgeStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isPurgeStatusVisible;

		[ObservableProperty]
		private bool isPurgeStatusError;

		// =================================================================
		// Device label (Better Stack live-tail identifier)
		// =================================================================

		/// <summary>
		/// User-editable copy of the device label. Two-way bound to the Entry
		/// on SettingsPage. Save is explicit (a button + command) rather than
		/// PropertyChanged-on-every-keystroke because rebuilding the Serilog
		/// pipeline is cheap but not free, and we don't want to thrash it
		/// while the user is mid-typing.
		/// </summary>
		[ObservableProperty]
		private string deviceLabel = string.Empty;

		/// <summary>The auto-default that would apply if the user clears the field.</summary>
		[ObservableProperty]
		private string deviceLabelPlaceholder = string.Empty;

		[ObservableProperty]
		private string deviceLabelStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isDeviceLabelStatusVisible;

		public SettingsViewModel(
			IConfigurationService configService,
			IDbContextFactory<UnityDbContext> dbContextFactory,
			RegistrationEventLog eventLog,
			IBetterStackLoggerController betterStackController)
		{
			_configService = configService;
			_dbContextFactory = dbContextFactory;
			_eventLog = eventLog;
			_betterStackController = betterStackController;

			// Seed the editable copy with whatever's currently in effect (either
			// the user-set value or the auto-default), and capture the auto-default
			// separately so we can show it as an Entry placeholder.
			DeviceLabel = _configService.DeviceLabel;
			DeviceLabelPlaceholder = _configService.DeviceLabel;
		}

		/// <summary>
		/// Persists the typed-in device label and rebuilds the Serilog
		/// pipeline so the new value flows through to Better Stack on the
		/// very next log event. Empty input clears the override and reverts
		/// to the auto-default.
		/// </summary>
		[RelayCommand]
		private void SaveDeviceLabel()
		{
			try
			{
				_configService.SetDeviceLabel(DeviceLabel);

				// Rebuild Log.Logger so the enricher picks up the new value.
				// Reconfigure is a full rebuild from the base-logger factory,
				// which re-reads Preferences on every invocation.
				var bsConfig = _configService.GetBetterStackConfiguration();
				_betterStackController.Reconfigure(bsConfig);

				// Reflect what's actually in effect now (auto-default if the
				// user cleared the field).
				var effective = _configService.DeviceLabel;
				DeviceLabel = effective;
				DeviceLabelPlaceholder = effective;

				ShowDeviceLabelStatus($"Saved. New logs will tag this device as \"{effective}\".");
				Logger.Information("Device label updated and logger rebuilt — now {DeviceLabel}", effective);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to save device label");
				ShowDeviceLabelStatus("Could not save device label — see logs.");
			}
		}

		private void ShowDeviceLabelStatus(string message)
		{
			DeviceLabelStatusMessage = message;
			IsDeviceLabelStatusVisible = true;
		}

		// =================================================================
		// Registration event log toggle
		// =================================================================

		/// <summary>
		/// Two-way bound to a Switch on SettingsPage. Reads/writes the
		/// underlying Preference via IConfigurationService so the value
		/// is shared with AttendanceService without any additional plumbing.
		/// Not an [ObservableProperty] because the backing store is
		/// Preferences, not a field — we don't need an INotifyPropertyChanged
		/// field holding stale state.
		/// </summary>
		public bool IsRegistrationEventLogEnabled
		{
			get => _configService.IsRegistrationEventLogEnabled;
			set
			{
				if (_configService.IsRegistrationEventLogEnabled == value) return;
				_configService.SetRegistrationEventLogEnabled(value);
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Two-way bound to a Switch on SettingsPage. When on, registering a
		/// group via the Verify flow also registers any intergroup position
		/// held by one of its members. See
		/// <see cref="IConfigurationService.IsAutoRegisterPositionsOnGroupEnabled"/>.
		/// Same "no local backing field" approach as the event log toggle:
		/// Preferences is the source of truth.
		/// </summary>
		public bool IsAutoRegisterPositionsOnGroupEnabled
		{
			get => _configService.IsAutoRegisterPositionsOnGroupEnabled;
			set
			{
				if (_configService.IsAutoRegisterPositionsOnGroupEnabled == value) return;
				_configService.SetAutoRegisterPositionsOnGroupEnabled(value);
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Two-way bound to a Switch on SettingsPage. When on, the verify-group
		/// flow takes a shortcut whenever a group has exactly one GSR: tapping
		/// "No" opens that GSR's edit form directly (skipping the picker), and
		/// tapping "Finished" on that edit form auto-registers attendance on
		/// return. See
		/// <see cref="IConfigurationService.IsSingleGsrShortcutEnabled"/>.
		/// Same "no local backing field" approach as the other toggles:
		/// Preferences is the source of truth.
		/// </summary>
		public bool IsSingleGsrShortcutEnabled
		{
			get => _configService.IsSingleGsrShortcutEnabled;
			set
			{
				if (_configService.IsSingleGsrShortcutEnabled == value) return;
				_configService.SetSingleGsrShortcutEnabled(value);
				OnPropertyChanged();
			}
		}

		// =================================================================
		// Navigation
		// =================================================================

		[RelayCommand]
		private async Task NavigateToMailSetting()
		{
			await ShowFeedback();
			await Shell.Current.GoToAsync(nameof(MailSettingsPage));
		}

		/// <summary>
		/// Navigate to the combined Integrations settings page. Replaces the
		/// previous two separate <c>NavigateToUnitySetting</c> and
		/// <c>NavigateToBetterStackSetting</c> commands — both Unity API and
		/// Better Stack configuration now live under a single page with an
		/// unsaved-changes guard.
		/// </summary>
		[RelayCommand]
		private async Task NavigateToIntegrationsSetting()
		{
			await ShowFeedback();
			await Shell.Current.GoToAsync(nameof(IntegrationsSettingsPage));
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

		// =================================================================
		// Reset Device
		// =================================================================

		/// <summary>
		/// Resets the device: purges all data from the local database AND
		/// deletes the registration event log if present. The two actions
		/// are logically paired — leaving the log behind after a database
		/// purge would cause the next startup replay to resurrect rows
		/// that no longer have corresponding groups/positions, producing
		/// "missing entity" warnings at best and data corruption at worst.
		///
		/// Command / property names are kept as <c>PurgeDatabase*</c> for
		/// backward compatibility with existing XAML bindings; the
		/// user-visible surface ("Reset Device") is handled in
		/// <see cref="Views.SettingsPage"/> and the dialog text below.
		/// </summary>
		[RelayCommand]
		private async Task PurgeDatabase()
		{
			bool confirmed = await Shell.Current.DisplayAlert(
				"Reset Device",
				"This will permanently delete ALL local data including groups, members, meetings, positions, and snapshots, and clear the registration event log.\n\nThis action cannot be undone. Are you sure?",
				"Yes, Reset Device",
				"No, Keep Data");

			if (!confirmed) return;

			try
			{
				IsPurging = true;
				ShowPurgeStatus("Resetting device...", false);

				using var dbContext = _dbContextFactory.CreateDbContext();
				await dbContext.PurgeDatabaseAsync();

				// Clear the active intergroup meeting selection. The
				// meeting ID is stored in Preferences and refers to a
				// row that we've just deleted — leaving it behind would
				// leave the app pointing at a meeting that no longer
				// exists. Same "log but don't fail" policy as the event
				// log deletion below: the DB purge has already succeeded.
				try
				{
					await _configService.SaveActiveIntergroupMeetingAsync(null);
				}
				catch (Exception ex)
				{
					Logger.Warning(ex, "Failed to clear active intergroup meeting during device reset");
				}

				// Delete the registration event log if it exists. We do
				// this after the DB purge succeeds, not before — if the
				// DB purge throws, the log is still a useful forensic
				// record of what the previous database contained.
				//
				// A failure here is logged but does NOT flip the status
				// to error: the database — the primary state — has
				// already been successfully wiped, and reporting "reset
				// failed" would mislead the operator into retrying a
				// destructive action that already completed.
				TryDeleteRegistrationLog();

				ShowPurgeStatus("Device reset successfully.", false);
				Logger.Information("Device reset successfully from Settings");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Device reset failed from Settings");
				ShowPurgeStatus($"Reset failed: {ex.Message}", true);
			}
			finally
			{
				IsPurging = false;
			}
		}

		private void TryDeleteRegistrationLog()
		{
			try
			{
				var path = _eventLog.LogPath;
				if (File.Exists(path))
				{
					File.Delete(path);
					Logger.Information("Registration event log deleted as part of device reset: {Path}", path);
				}
			}
			catch (Exception ex)
			{
				// Swallow — see comment in PurgeDatabase. The DB has
				// already been purged; surfacing this as a user-facing
				// failure would do more harm than good.
				Logger.Warning(ex, "Failed to delete registration event log during device reset");
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