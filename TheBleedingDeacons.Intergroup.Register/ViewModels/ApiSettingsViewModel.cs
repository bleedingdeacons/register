using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// The API settings page: the Freedom sign-in, and the Unity and Better
	/// Stack connections Freedom supplies.
	///
	/// <para>The connections are read-only. They come from Freedom and
	/// nowhere else (see <see cref="FreedomSettings"/>), so the page shows
	/// where the tablet is pointed — the Unity site in full, a key or token
	/// only as set or not set — and keeps a Test button for each, which is
	/// what an operator needs when something is not arriving.</para>
	/// </summary>
	public partial class ApiSettingsViewModel : ObservableObject
	{
		private static readonly ILogger Logger = AppLogger.ForContext<ApiSettingsViewModel>();

		private readonly IConfigurationService _configService;

		private UnityConfiguration _unity = new();
		private BetterStackConfiguration _betterStack = new();

		public ApiSettingsViewModel(
			IConfigurationService configService,
			TheBleedingDeacons.Freedom.Client.FreedomClient? freedom = null)
		{
			_configService = configService;
			_freedom = freedom;
			RefreshFreedomStatus();
			LoadConfigurationAsync().SafeFireAndForget("LoadApiSettingsConfig");
		}

		// ─── Unity ────────────────────────────────────────────────────────

		[ObservableProperty]
		private string unityBaseUrl = string.Empty;

		[ObservableProperty]
		private string unityApiKey = string.Empty;

		[ObservableProperty]
		private bool isUnityConfigured;

		[ObservableProperty]
		private bool isUnityTesting;

		[ObservableProperty]
		private string unityStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isUnityStatusVisible;

		[ObservableProperty]
		private bool isUnityStatusError;

		// ─── Better Stack ─────────────────────────────────────────────────

		[ObservableProperty]
		private string betterStackEndpoint = string.Empty;

		[ObservableProperty]
		private string betterStackSourceToken = string.Empty;

		[ObservableProperty]
		private bool isBetterStackConfigured;

		[ObservableProperty]
		private bool isBetterStackTesting;

		[ObservableProperty]
		private string betterStackStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isBetterStackStatusVisible;

		[ObservableProperty]
		private bool isBetterStackStatusError;

		// ─── Unity test ───────────────────────────────────────────────────

		[RelayCommand]
		private async Task TestUnityConnectionAsync()
		{
			try
			{
				IsUnityTesting = true;
				HideUnityStatus();

				await LoadConfigurationAsync();
				if (!IsUnityConfigured)
				{
					ShowUnityStatus("Unity is not set up on the site for this tablet. Sign in to Freedom above.", true);
					return;
				}

				using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				httpClient.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _unity.ApiKey.Trim());

				var testUrl = _unity.BaseUrl.TrimEnd('/') + "/wp-json/integrity/v1/positions?per_page=1";
				var response = await httpClient.GetAsync(testUrl);

				if (response.IsSuccessStatusCode)
				{
					ShowUnityStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowUnityStatus("Authentication failed. Check the API key on the site.", true);
				}
				else
				{
					ShowUnityStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowUnityStatus("Connection timed out. Check the site address on the site.", true);
			}
			catch (HttpRequestException ex)
			{
				ShowUnityStatus($"Connection failed: {ex.Message}", true);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Unity connection test failed");
				ShowUnityStatus($"Test failed: {ex.Message}", true);
			}
			finally
			{
				IsUnityTesting = false;
			}
		}

		// ─── Better Stack test ────────────────────────────────────────────

		[RelayCommand]
		private async Task TestBetterStackConnectionAsync()
		{
			try
			{
				IsBetterStackTesting = true;
				HideBetterStackStatus();

				await LoadConfigurationAsync();
				if (!IsBetterStackConfigured)
				{
					ShowBetterStackStatus("Better Stack is not set up on the site for this tablet, or its endpoint is not https.", true);
					return;
				}

				using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				httpClient.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _betterStack.SourceToken);

				// IsValid has already held the endpoint to https, so the token
				// never goes out in cleartext.
				var response = await httpClient.GetAsync(_betterStack.Endpoint);

				if (response.IsSuccessStatusCode)
				{
					ShowBetterStackStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowBetterStackStatus("Authentication failed. Check the source token on the site.", true);
				}
				else
				{
					ShowBetterStackStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowBetterStackStatus("Connection timed out. Check the endpoint on the site.", true);
			}
			catch (HttpRequestException ex)
			{
				ShowBetterStackStatus($"Connection failed: {ex.Message}", true);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Better Stack connection test failed");
				ShowBetterStackStatus($"Test failed: {ex.Message}", true);
			}
			finally
			{
				IsBetterStackTesting = false;
			}
		}

		// ─── Load ─────────────────────────────────────────────────────────

		private async Task LoadConfigurationAsync()
		{
			_unity = await _configService.LoadUnityConfigurationAsync();
			_betterStack = await _configService.LoadBetterStackConfigurationAsync();

			UnityBaseUrl = string.IsNullOrWhiteSpace(_unity.BaseUrl) ? "Not set" : _unity.BaseUrl;
			UnityApiKey = string.IsNullOrEmpty(_unity.ApiKey) ? "Not set" : "Set";
			IsUnityConfigured = _unity.IsValid();

			BetterStackEndpoint = string.IsNullOrWhiteSpace(_betterStack.Endpoint) ? "Not set" : _betterStack.Endpoint;
			BetterStackSourceToken = string.IsNullOrEmpty(_betterStack.SourceToken) ? "Not set" : "Set";
			IsBetterStackConfigured = _betterStack.IsValid();
		}

		// ─── Status helpers ───────────────────────────────────────────────

		private void ShowUnityStatus(string message, bool isError)
		{
			UnityStatusMessage = isError ? $"❌ {message}" : $"✅ {message}";
			IsUnityStatusError = isError;
			IsUnityStatusVisible = true;

			if (!isError)
			{
				Task.Delay(3000).ContinueWith(_ => HideUnityStatus());
			}
		}

		private void HideUnityStatus()
		{
			IsUnityStatusVisible = false;
			UnityStatusMessage = string.Empty;
		}

		private void ShowBetterStackStatus(string message, bool isError)
		{
			BetterStackStatusMessage = isError ? $"❌ {message}" : $"✅ {message}";
			IsBetterStackStatusError = isError;
			IsBetterStackStatusVisible = true;

			if (!isError)
			{
				Task.Delay(3000).ContinueWith(_ => HideBetterStackStatus());
			}
		}

		private void HideBetterStackStatus()
		{
			IsBetterStackStatusVisible = false;
			BetterStackStatusMessage = string.Empty;
		}
	}
}
