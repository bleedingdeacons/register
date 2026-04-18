using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	public partial class MailSettingsViewModel : ObservableObject
	{
		private static readonly ILogger Logger = AppLogger.ForContext<MailSettingsViewModel>();

		private readonly IConfigurationService _configService;
		private readonly IMailService _mailService;

		public MailSettingsViewModel(IConfigurationService configService, IMailService mailService)
		{
			_configService = configService;
			_mailService = mailService;

			LoadConfigurationAsync().SafeFireAndForget("LoadMailConfig");
		}

		[ObservableProperty]
		private string host = string.Empty;

		[ObservableProperty]
		private string port = "587";

		[ObservableProperty]
		private string username = string.Empty;

		[ObservableProperty]
		private string password = string.Empty;

		[ObservableProperty]
		private bool enableSsl = true;

		[ObservableProperty]
		private string fromDisplayName = string.Empty;

		[ObservableProperty]
		private bool isTestingConnection = false;

		[ObservableProperty]
		private bool isSaving = false;

		[ObservableProperty]
		private string statusMessage = string.Empty;

		[ObservableProperty]
		private bool isStatusVisible = false;

		[ObservableProperty]
		private bool isStatusError = false;

		// Computed properties for validation
		public bool IsFormValid => !string.IsNullOrWhiteSpace(Host) &&
								  !string.IsNullOrWhiteSpace(Username) &&
								  !string.IsNullOrWhiteSpace(Password) &&
								  int.TryParse(Port, out var portNum) && portNum > 0;

		[RelayCommand]
		private async Task TestConnectionAsync()
		{
			try
			{
				IsTestingConnection = true;
				HideStatus();

				if (!IsFormValid)
				{
					ShowStatus("Please fill in all required fields", true);
					return;
				}

				var tempConfig = CreateConfigFromForm();
				var testResult = await _mailService.TestSmtpConnectionAsync(tempConfig);

				if (testResult)
				{
					ShowStatus("✅ SMTP connection test successful!", false);
				}
				else
				{
					ShowStatus("❌ Could not connect to SMTP server. Please check your settings.", true);
				}
			}
			catch (Exception ex)
			{
				ShowStatus($"❌ Test failed: {ex.Message}", true);
			}
			finally
			{
				IsTestingConnection = false;
			}
		}

		[RelayCommand]
		private async Task SaveSettingsAsync()
		{
			try
			{
				IsSaving = true;
				HideStatus();

				if (!IsFormValid)
				{
					ShowStatus("Please fill in all required fields", true);
					return;
				}

				var config = CreateConfigFromForm();

				// Persist first — if this fails, the running MailKitService is untouched
				// and the user's old settings remain in effect.
				await _configService.SaveSmtpConfigurationAsync(config);

				// Push the new config into the running singleton so it takes effect
				// immediately instead of waiting for app restart. UpdateConfigurationAsync
				// also resets the circuit breaker (see MailKitService) so the next
				// queue tick will attempt delivery with the fresh credentials.
				await _mailService.UpdateConfigurationAsync(config);

				Logger.Information("SMTP configuration updated for {Host}:{Port}",
					config.ToLogSafe().Host, config.ToLogSafe().Port);

				ShowStatus("✅ SMTP settings saved successfully!", false);

				// Notify any subscribers (e.g. status pages) that settings changed.
				WeakReferenceMessenger.Default.Send(new SettingsSavedMessage { Configuration = config });
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to save SMTP settings");
				ShowStatus($"❌ Failed to save settings: {ex.Message}", true);
			}
			finally
			{
				IsSaving = false;
			}
		}

		[RelayCommand]
		private void SetGmailDefaults()
		{
			Host = "smtp.gmail.com";
			Port = "587";
			EnableSsl = true;
			ShowStatus("📧 Gmail settings applied. Don't forget to use an App Password!", false);
		}

		[RelayCommand]
		private void SetOutlookDefaults()
		{
			Host = "smtp-mail.outlook.com";
			Port = "587";
			EnableSsl = true;
			ShowStatus("📧 Outlook settings applied!", false);
		}

		[RelayCommand]
		private void SetYahooDefaults()
		{
			Host = "smtp.mail.yahoo.com";
			Port = "587";
			EnableSsl = true;
			ShowStatus("📧 Yahoo settings applied!", false);
		}

		private async Task LoadConfigurationAsync()
		{
			var config = await _configService.LoadSmtpConfigurationAsync();

			// Use SetProperty to trigger PropertyChanged notifications
			SetProperty(ref host, config.Host, nameof(Host));
			SetProperty(ref port, config.Port.ToString(), nameof(Port));
			SetProperty(ref username, config.Username, nameof(Username));
			SetProperty(ref password, config.Password, nameof(Password));
			SetProperty(ref enableSsl, config.EnableSsl, nameof(EnableSsl));
			SetProperty(ref fromDisplayName, config.FromDisplayName, nameof(FromDisplayName));

			// Trigger validation check
			OnPropertyChanged(nameof(IsFormValid));
		}

		private SmtpConfiguration CreateConfigFromForm()
		{
			return new SmtpConfiguration
			{
				Host = Host.Trim(),
				Port = int.TryParse(Port, out int port) ? port : 587,
				Username = Username.Trim(),
				Password = Password,
				EnableSsl = EnableSsl,
				FromDisplayName = FromDisplayName.Trim(),
				TimeoutSeconds = 30
			};
		}

		private void ShowStatus(string message, bool isError)
		{
			StatusMessage = message;
			IsStatusError = isError;
			IsStatusVisible = true;

			// Auto-hide success messages after 3 seconds
			if (!isError)
			{
				Task.Delay(3000).ContinueWith(_ => HideStatus());
			}
		}

		private void HideStatus()
		{
			IsStatusVisible = false;
			StatusMessage = string.Empty;
		}

		// Property change notifications for validation
		partial void OnHostChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
		partial void OnUsernameChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
		partial void OnPasswordChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
		partial void OnPortChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
	}

	// Message for notifying when settings are saved
	public class SettingsSavedMessage
	{
		public SmtpConfiguration? Configuration { get; set; }
	}
}