using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// The SMTP settings this tablet is using, and a way to test them.
	///
	/// <para>Read-only. Every value comes from Freedom (see
	/// <see cref="FreedomSettings"/>), so there is nothing to type and nothing
	/// to save: a change is made on the site and arrives with the next sync.
	/// What is left here is what an operator at the door actually needs — to
	/// see which server the tablet is pointed at, and whether it answers.</para>
	/// </summary>
	public partial class MailSettingsViewModel : BaseViewModel
	{
		private static readonly ILogger Logger = AppLogger.ForContext<MailSettingsViewModel>();

		private readonly IConfigurationService _configService;
		private readonly IEmailService _emailService;

		public MailSettingsViewModel(IConfigurationService configService, IEmailService emailService)
		{
			_configService = configService;
			_emailService = emailService;

			LoadConfigurationAsync().SafeFireAndForget("LoadMailConfig");
		}

		[ObservableProperty]
		private string host = string.Empty;

		[ObservableProperty]
		private string port = string.Empty;

		[ObservableProperty]
		private string username = string.Empty;

		[ObservableProperty]
		private string password = string.Empty;

		[ObservableProperty]
		private string security = string.Empty;

		[ObservableProperty]
		private string fromDisplayName = string.Empty;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsNotConfigured))]
		private bool isConfigured;

		public bool IsNotConfigured => !IsConfigured;

		[ObservableProperty]
		private bool isTestingConnection;

		[ObservableProperty]
		private string statusMessage = string.Empty;

		[ObservableProperty]
		private bool isStatusVisible;

		[ObservableProperty]
		private bool isStatusError;

		[RelayCommand]
		private async Task TestConnectionAsync()
		{
			try
			{
				IsTestingConnection = true;
				HideStatus();

				var config = await LoadConfigurationAsync();
				if (!config.IsValid())
				{
					ShowStatus("SMTP is not set up on the site for this tablet. Sign in to Freedom under API Settings.", true);
					return;
				}

				var testResult = await _emailService.TestSmtpConnectionAsync(config);

				if (testResult)
				{
					ShowStatus("✅ SMTP connection test successful!", false);
				}
				else
				{
					ShowStatus("❌ Could not connect to the SMTP server. Check the settings on the site.", true);
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "SMTP connection test failed");
				ShowStatus($"❌ Test failed: {ex.Message}", true);
			}
			finally
			{
				IsTestingConnection = false;
			}
		}

		private async Task<SmtpConfiguration> LoadConfigurationAsync()
		{
			var config = await _configService.LoadSmtpConfigurationAsync();

			Host = Shown(config.Host);
			Port = config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
			Username = Shown(config.Username);
			Password = string.IsNullOrEmpty(config.Password) ? "Not set" : "Set";
			Security = config.EnableSsl ? "TLS" : "None";
			FromDisplayName = Shown(config.FromDisplayName);
			IsConfigured = config.IsValid();

			return config;
		}

		private static string Shown(string? value) => string.IsNullOrWhiteSpace(value) ? "Not set" : value;

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
	}
}
