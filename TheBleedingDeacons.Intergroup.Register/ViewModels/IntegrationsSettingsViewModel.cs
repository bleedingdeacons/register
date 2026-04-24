using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// Combined ViewModel for the Integrations settings page, backing both the
	/// Unity API section and the Better Stack logging section on a single page.
	///
	/// Each section keeps its own Test / Save commands and its own status bar
	/// because the underlying operations are different (Unity hits a WordPress
	/// REST endpoint; Better Stack tears down and rebuilds the Serilog sink).
	/// The only thing shared is the <see cref="HasUnsavedChanges"/> flag, which
	/// is true when *either* form differs from the last-loaded/saved snapshot
	/// — this drives the navigation-away prompt on the page.
	/// </summary>
	public partial class IntegrationsSettingsViewModel : ObservableObject
	{
		private static readonly ILogger Logger = AppLogger.ForContext<IntegrationsSettingsViewModel>();

		private readonly IConfigurationService _configService;
		private readonly IBetterStackLoggerController _loggerController;

		// Snapshots of the last-loaded / last-saved values. We compare the
		// current form fields against these to decide if there are unsaved
		// changes. Initialised by LoadConfigurationAsync and refreshed on
		// each successful save.
		private string _unityBaseUrlSnapshot = string.Empty;
		private string _unityApiKeySnapshot = string.Empty;
		private string _betterStackEndpointSnapshot = string.Empty;
		private string _betterStackSourceTokenSnapshot = string.Empty;

		// Skip dirty-checking while we're loading or resetting the form from
		// a persisted snapshot — otherwise every field assignment during load
		// would flip HasUnsavedChanges to true and back again.
		private bool _suppressDirtyCheck;

		public IntegrationsSettingsViewModel(
			IConfigurationService configService,
			IBetterStackLoggerController loggerController)
		{
			_configService = configService;
			_loggerController = loggerController;
			LoadConfigurationAsync().SafeFireAndForget("LoadIntegrationsConfig");
		}

		// ─── Unity fields ─────────────────────────────────────────────────

		[ObservableProperty]
		private string unityBaseUrl = string.Empty;

		[ObservableProperty]
		private string unityApiKey = string.Empty;

		[ObservableProperty]
		private bool isUnitySaving;

		[ObservableProperty]
		private bool isUnityTesting;

		[ObservableProperty]
		private string unityStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isUnityStatusVisible;

		[ObservableProperty]
		private bool isUnityStatusError;

		public bool IsUnityFormValid =>
			!string.IsNullOrWhiteSpace(UnityBaseUrl) &&
			!string.IsNullOrWhiteSpace(UnityApiKey) &&
			Uri.TryCreate(UnityBaseUrl, UriKind.Absolute, out _);

		// ─── Better Stack fields ──────────────────────────────────────────

		[ObservableProperty]
		private string betterStackEndpoint = string.Empty;

		[ObservableProperty]
		private string betterStackSourceToken = string.Empty;

		[ObservableProperty]
		private bool isBetterStackSaving;

		[ObservableProperty]
		private bool isBetterStackTesting;

		[ObservableProperty]
		private string betterStackStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isBetterStackStatusVisible;

		[ObservableProperty]
		private bool isBetterStackStatusError;

		public bool IsBetterStackFormValid =>
			!string.IsNullOrWhiteSpace(BetterStackSourceToken) &&
			!string.IsNullOrWhiteSpace(BetterStackEndpoint) &&
			Uri.TryCreate(BetterStackEndpoint, UriKind.Absolute, out _);

		// ─── Unsaved-changes flag ─────────────────────────────────────────

		[ObservableProperty]
		private bool hasUnsavedChanges;

		// ─── Unity commands ───────────────────────────────────────────────

		[RelayCommand]
		private async Task TestUnityConnectionAsync()
		{
			try
			{
				IsUnityTesting = true;
				HideUnityStatus();

				if (!IsUnityFormValid)
				{
					ShowUnityStatus("Please fill in all required fields", true);
					return;
				}

				using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				httpClient.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", UnityApiKey.Trim());

				var testUrl = UnityBaseUrl.TrimEnd('/') + "/wp-json/integrity/v1/positions?per_page=1";
				var response = await httpClient.GetAsync(testUrl);

				if (response.IsSuccessStatusCode)
				{
					ShowUnityStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowUnityStatus("Authentication failed. Please check your API key.", true);
				}
				else
				{
					ShowUnityStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowUnityStatus("Connection timed out. Please check the URL.", true);
			}
			catch (HttpRequestException ex)
			{
				ShowUnityStatus($"Connection failed: {ex.Message}", true);
			}
			catch (Exception ex)
			{
				ShowUnityStatus($"Test failed: {ex.Message}", true);
			}
			finally
			{
				IsUnityTesting = false;
			}
		}

		[RelayCommand]
		private async Task SaveUnitySettingsAsync()
		{
			try
			{
				IsUnitySaving = true;
				HideUnityStatus();

				if (!IsUnityFormValid)
				{
					ShowUnityStatus("Please fill in all required fields", true);
					return;
				}

				var config = new UnityConfiguration
				{
					BaseUrl = UnityBaseUrl.Trim().TrimEnd('/'),
					ApiKey = UnityApiKey.Trim(),
				};

				await _configService.SaveUnityConfigurationAsync(config);

				// Update the snapshot to the just-saved normalised values so
				// that the dirty check returns false immediately after a save.
				_unityBaseUrlSnapshot = config.BaseUrl;
				_unityApiKeySnapshot = config.ApiKey;

				// Reflect the normalised values back into the form so the user
				// sees what was actually persisted.
				_suppressDirtyCheck = true;
				UnityBaseUrl = config.BaseUrl;
				UnityApiKey = config.ApiKey;
				_suppressDirtyCheck = false;

				RecomputeHasUnsavedChanges();

				ShowUnityStatus("Unity API settings saved successfully!", false);
				Logger.Information("Unity API settings saved for {BaseUrl}", config.ToLogSafe().BaseUrl);
			}
			catch (Exception ex)
			{
				ShowUnityStatus($"Failed to save settings: {ex.Message}", true);
				Logger.Error(ex, "Failed to save Unity configuration");
			}
			finally
			{
				IsUnitySaving = false;
			}
		}

		// ─── Better Stack commands ────────────────────────────────────────

		[RelayCommand]
		private async Task TestBetterStackConnectionAsync()
		{
			try
			{
				IsBetterStackTesting = true;
				HideBetterStackStatus();

				if (!IsBetterStackFormValid)
				{
					ShowBetterStackStatus("Please fill in all required fields", true);
					return;
				}

				using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

				httpClient.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", BetterStackSourceToken.Trim());

				var response = await httpClient.GetAsync(BetterStackEndpoint.Trim().TrimEnd('/'));

				if (response.IsSuccessStatusCode)
				{
					ShowBetterStackStatus("Connection successful!", false);
				}
				else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
						 response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				{
					ShowBetterStackStatus("Authentication failed. Please check your source token.", true);
				}
				else
				{
					ShowBetterStackStatus($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}", true);
				}
			}
			catch (TaskCanceledException)
			{
				ShowBetterStackStatus("Connection timed out. Please check the endpoint URL.", true);
			}
			catch (HttpRequestException ex)
			{
				ShowBetterStackStatus($"Connection failed: {ex.Message}", true);
			}
			catch (Exception ex)
			{
				ShowBetterStackStatus($"Test failed: {ex.Message}", true);
			}
			finally
			{
				IsBetterStackTesting = false;
			}
		}

		[RelayCommand]
		private async Task SaveBetterStackSettingsAsync()
		{
			try
			{
				IsBetterStackSaving = true;
				HideBetterStackStatus();

				if (!IsBetterStackFormValid)
				{
					ShowBetterStackStatus("Please fill in all required fields", true);
					return;
				}

				var config = new BetterStackConfiguration
				{
					SourceToken = BetterStackSourceToken.Trim(),
					Endpoint = BetterStackEndpoint.Trim().TrimEnd('/')
				};

				// Persist first so that if the save fails, the running pipeline
				// is untouched and a relaunch won't pick up partial state.
				await _configService.SaveBetterStackConfigurationAsync(config);

				// Tear down the previous Serilog pipeline and rebuild with the
				// new credentials. Without this, the app continues shipping to
				// the previous endpoint/token until restart.
				_loggerController.Reconfigure(config);

				// Update snapshot and reflect normalised values in the form.
				_betterStackSourceTokenSnapshot = config.SourceToken;
				_betterStackEndpointSnapshot = config.Endpoint;

				_suppressDirtyCheck = true;
				BetterStackSourceToken = config.SourceToken;
				BetterStackEndpoint = config.Endpoint;
				_suppressDirtyCheck = false;

				RecomputeHasUnsavedChanges();

				ShowBetterStackStatus("Better Stack settings saved successfully!", false);
				Logger.Information("Better Stack settings saved for {Endpoint}", config.ToLogSafe().Endpoint);
			}
			catch (Exception ex)
			{
				ShowBetterStackStatus($"Failed to save settings: {ex.Message}", true);
				Logger.Error(ex, "Failed to save Better Stack configuration");
			}
			finally
			{
				IsBetterStackSaving = false;
			}
		}

		// ─── Load / dirty tracking ────────────────────────────────────────

		private async Task LoadConfigurationAsync()
		{
			_suppressDirtyCheck = true;
			try
			{
				var unity = await _configService.LoadUnityConfigurationAsync();
				var betterStack = await _configService.LoadBetterStackConfigurationAsync();

				SetProperty(ref unityBaseUrl, unity.BaseUrl, nameof(UnityBaseUrl));
				SetProperty(ref unityApiKey, unity.ApiKey, nameof(UnityApiKey));
				SetProperty(ref betterStackSourceToken, betterStack.SourceToken, nameof(BetterStackSourceToken));
				SetProperty(ref betterStackEndpoint, betterStack.Endpoint, nameof(BetterStackEndpoint));

				_unityBaseUrlSnapshot = unity.BaseUrl ?? string.Empty;
				_unityApiKeySnapshot = unity.ApiKey ?? string.Empty;
				_betterStackSourceTokenSnapshot = betterStack.SourceToken ?? string.Empty;
				_betterStackEndpointSnapshot = betterStack.Endpoint ?? string.Empty;

				OnPropertyChanged(nameof(IsUnityFormValid));
				OnPropertyChanged(nameof(IsBetterStackFormValid));
			}
			finally
			{
				_suppressDirtyCheck = false;
				HasUnsavedChanges = false;
			}
		}

		private void RecomputeHasUnsavedChanges()
		{
			if (_suppressDirtyCheck) return;

			// Trim during comparison to match the normalisation that happens
			// on save. Without this, a trailing space the user didn't mean to
			// add would trigger the unsaved-changes prompt.
			HasUnsavedChanges =
				!string.Equals((UnityBaseUrl ?? string.Empty).Trim().TrimEnd('/'),
							   _unityBaseUrlSnapshot, StringComparison.Ordinal) ||
				!string.Equals((UnityApiKey ?? string.Empty).Trim(),
							   _unityApiKeySnapshot, StringComparison.Ordinal) ||
				!string.Equals((BetterStackSourceToken ?? string.Empty).Trim(),
							   _betterStackSourceTokenSnapshot, StringComparison.Ordinal) ||
				!string.Equals((BetterStackEndpoint ?? string.Empty).Trim().TrimEnd('/'),
							   _betterStackEndpointSnapshot, StringComparison.Ordinal);
		}

		// ─── Status helpers ───────────────────────────────────────────────

		private void ShowUnityStatus(string message, bool isError)
		{
			UnityStatusMessage = isError ? $"\u274c {message}" : $"\u2705 {message}";
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
			BetterStackStatusMessage = isError ? $"\u274c {message}" : $"\u2705 {message}";
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

		// ─── Property-changed hooks ───────────────────────────────────────
		//
		// Every field that forms part of either IsFormValid or the dirty-check
		// comparison needs to re-run both. Keeping this explicit rather than
		// using a base-class observer because the two sections feed different
		// validation flags.

		partial void OnUnityBaseUrlChanged(string value)
		{
			OnPropertyChanged(nameof(IsUnityFormValid));
			RecomputeHasUnsavedChanges();
		}

		partial void OnUnityApiKeyChanged(string value)
		{
			OnPropertyChanged(nameof(IsUnityFormValid));
			RecomputeHasUnsavedChanges();
		}

		partial void OnBetterStackSourceTokenChanged(string value)
		{
			OnPropertyChanged(nameof(IsBetterStackFormValid));
			RecomputeHasUnsavedChanges();
		}

		partial void OnBetterStackEndpointChanged(string value)
		{
			OnPropertyChanged(nameof(IsBetterStackFormValid));
			RecomputeHasUnsavedChanges();
		}
	}
}
