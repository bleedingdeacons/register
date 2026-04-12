using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	public partial class UnitySettingsViewModel : ObservableObject
	{
		private static readonly ILogger Logger = AppLogger.ForContext<UnitySettingsViewModel>();

		private readonly IConfigurationService _configService;

		public UnitySettingsViewModel(IConfigurationService configService)
		{
			_configService = configService;
			LoadConfigurationAsync().SafeFireAndForget("LoadUnityConfig");
		}

		[ObservableProperty]
		private string baseUrl = string.Empty;

		[ObservableProperty]
		private string apiKey = string.Empty;

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

		public bool IsFormValid => !string.IsNullOrWhiteSpace(BaseUrl) &&
								   !string.IsNullOrWhiteSpace(ApiKey) &&
								   Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);

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
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey.Trim());

				var testUrl = BaseUrl.TrimEnd('/') + "/wp-json/integrity/v1/positions?per_page=1";
				var response = await httpClient.GetAsync(testUrl);

				if (response.IsSuccessStatusCode)
				{
					ShowStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowStatus("Authentication failed. Please check your API key.", true);
				}
				else
				{
					ShowStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowStatus("Connection timed out. Please check the URL.", true);
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

				var config = new UnityConfiguration
				{
					BaseUrl = BaseUrl.Trim().TrimEnd('/'),
					ApiKey = ApiKey.Trim(),
				};

				await _configService.SaveUnityConfigurationAsync(config);
				ShowStatus("Unity API settings saved successfully!", false);

				Logger.Information("Unity API settings saved for {BaseUrl}", config.ToLogSafe().BaseUrl);
			}
			catch (Exception ex)
			{
				ShowStatus($"Failed to save settings: {ex.Message}", true);
				Logger.Error(ex, "Failed to save Unity configuration");
			}
			finally
			{
				IsSaving = false;
			}
		}

		private async Task LoadConfigurationAsync()
		{
			var config = await _configService.LoadUnityConfigurationAsync();

			SetProperty(ref baseUrl, config.BaseUrl, nameof(BaseUrl));
			SetProperty(ref apiKey, config.ApiKey, nameof(ApiKey));

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

		partial void OnBaseUrlChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
		partial void OnApiKeyChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
	}
}