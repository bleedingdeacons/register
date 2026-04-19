using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	public partial class BetterStackSettingsViewModel : ObservableObject
	{
		private static readonly ILogger Logger = AppLogger.ForContext<BetterStackSettingsViewModel>();

		private readonly IConfigurationService _configService;
		private readonly IBetterStackLoggerController _loggerController;

		public BetterStackSettingsViewModel(
			IConfigurationService configService,
			IBetterStackLoggerController loggerController)
		{
			_configService = configService;
			_loggerController = loggerController;
			LoadConfigurationAsync().SafeFireAndForget("LoadBetterStackConfig");
		}

		[ObservableProperty]
		private string sourceToken = string.Empty;

		[ObservableProperty]
		private string endpoint = string.Empty;

		[ObservableProperty]
		private bool isSaving = false;

		[ObservableProperty]
		private bool isTesting = false;

		[ObservableProperty]
		private string statusMessage = string.Empty;

		[ObservableProperty]
		private bool isStatusVisible = false;

		[ObservableProperty]
		private bool isStatusError = false;

		public bool IsFormValid => !string.IsNullOrWhiteSpace(SourceToken) &&
								   !string.IsNullOrWhiteSpace(Endpoint) &&
								   Uri.TryCreate(Endpoint, UriKind.Absolute, out _);

		[RelayCommand]
		private async Task TestConnectionAsync()
		{
			try
			{
				IsTesting = true;
				HideStatus();

				if (!IsFormValid)
				{
					ShowStatus("Please fill in all required fields", true);
					return;
				}

				using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

				httpClient.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SourceToken.Trim());

				var response = await httpClient.GetAsync(Endpoint.Trim().TrimEnd('/'));

				if (response.IsSuccessStatusCode)
				{
					ShowStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowStatus("Authentication failed. Please check your source token.", true);
				}
				else
				{
					ShowStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowStatus("Connection timed out. Please check the endpoint URL.", true);
			}
			catch (HttpRequestException ex)
			{
				ShowStatus($"Connection failed: {ex.Message}", true);
			}
			catch (Exception ex)
			{
				ShowStatus($"Test failed: {ex.Message}", true);
			}
			finally
			{
				IsTesting = false;
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

				var config = new BetterStackConfiguration
				{
					SourceToken = SourceToken.Trim(),
					Endpoint = Endpoint.Trim().TrimEnd('/')
				};

				// Persist first so that if the save fails, the running pipeline
				// is untouched and a relaunch won't pick up partial state.
				await _configService.SaveBetterStackConfigurationAsync(config);

				// Tear down the previous Serilog pipeline and rebuild with the
				// new credentials. Without this, the app continues shipping to
				// the previous endpoint/token until restart. Reconfigure is
				// synchronous and serialised internally, so it's safe to call
				// directly from this async command.
				_loggerController.Reconfigure(config);

				ShowStatus("Better Stack settings saved successfully!", false);

				Logger.Information("Better Stack settings saved for {Endpoint}", config.ToLogSafe().Endpoint);
			}
			catch (Exception ex)
			{
				ShowStatus($"Failed to save settings: {ex.Message}", true);
				Logger.Error(ex, "Failed to save Better Stack configuration");
			}
			finally
			{
				IsSaving = false;
			}
		}

		private async Task LoadConfigurationAsync()
		{
			var config = await _configService.LoadBetterStackConfigurationAsync();

			SetProperty(ref sourceToken, config.SourceToken, nameof(SourceToken));
			SetProperty(ref endpoint, config.Endpoint, nameof(Endpoint));

			OnPropertyChanged(nameof(IsFormValid));
		}

		private void ShowStatus(string message, bool isError)
		{
			StatusMessage = isError ? $"\u274c {message}" : $"\u2705 {message}";
			IsStatusError = isError;
			IsStatusVisible = true;

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

		partial void OnSourceTokenChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
		partial void OnEndpointChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
	}
}