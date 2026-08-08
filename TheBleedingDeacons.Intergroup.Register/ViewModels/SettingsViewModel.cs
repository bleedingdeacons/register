using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Models;
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
		private readonly IScrutinyClient _scrutinyClient;
		private readonly IPrivacyPolicyCache _privacyPolicyCache;

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

		// =================================================================
		// Compliance email (recipient used by the compliance service)
		// =================================================================

		/// <summary>
		/// User-editable copy of the compliance email address. Two-way
		/// bound to the Entry inside the Privacy Policy section of
		/// SettingsPage — the address lives there because it's the
		/// recipient for audit-trail copies of acceptance / revocation
		/// events tied to the active policy shown in that card. Save is
		/// explicit (a button + command) rather than
		/// PropertyChanged-on-every-keystroke so we can validate the
		/// address on commit and surface a single status message — green
		/// on success, red on a validation failure or persistence error.
		/// </summary>
		[ObservableProperty]
		private string complianceEmail = string.Empty;

		[ObservableProperty]
		private string complianceEmailStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isComplianceEmailStatusVisible;

		[ObservableProperty]
		private bool isComplianceEmailStatusError;

		// =================================================================
		// Active privacy policy (read from on-device Scrutiny cache)
		// =================================================================
		//
		// The Settings page reads the cached policy populated by the
		// sync stage. It does NOT call Scrutiny on every page-load,
		// because the page can be opened mid-meeting on a device that
		// has no internet at this moment, and we want it to show
		// useful information in that case.
		//
		// The Refresh button does a live Scrutiny fetch + cache update
		// — same code path as the sync-stage refresh — so the operator
		// has a way to manually pull the latest active policy when
		// they're online and want to confirm what's published.

		[ObservableProperty]
		private string privacyPolicyTitle = string.Empty;

		[ObservableProperty]
		private string privacyPolicyVersion = string.Empty;

		[ObservableProperty]
		private string privacyPolicyModified = string.Empty;

		/// <summary>
		/// Local "last refreshed N minutes ago" string. Populated from
		/// <see cref="CachedPrivacyPolicy.CachedAt"/>. Lets the operator
		/// see at a glance how stale the cached value is when they're
		/// working offline.
		/// </summary>
		[ObservableProperty]
		private string privacyPolicyCachedAt = string.Empty;

		/// <summary>
		/// True only when the cache contains a populated entry. Drives
		/// the visibility of the details block in the XAML so we don't
		/// show empty fields when no sync has ever happened on this
		/// device.
		/// </summary>
		[ObservableProperty]
		private bool hasActivePrivacyPolicy;

		[ObservableProperty]
		private bool isPrivacyPolicyLoading;

		[ObservableProperty]
		private string privacyPolicyStatusMessage = string.Empty;

		[ObservableProperty]
		private bool isPrivacyPolicyStatusVisible;

		[ObservableProperty]
		private bool isPrivacyPolicyStatusError;

		// =================================================================
		// Build identity
		// =================================================================

		/// <summary>
		/// Version, build number and .NET runtime of the running app — e.g.
		/// "1.0.26 (build 1) — .NET 10.0.10". A plain get-only property rather
		/// than an <c>[ObservableProperty]</c> because none of it can change
		/// while the process is alive, so there is nothing to notify about.
		/// Shared with the startup log banner via <see cref="BuildInfo"/> so
		/// the screen and the logs can never disagree. Kept as an instance
		/// member (despite reading only static state) so the Settings page can
		/// bind it through its <c>x:DataType</c> like every other value there.
		/// </summary>
		public string BuildSummary => BuildInfo.Summary;

		public SettingsViewModel(
			IConfigurationService configService,
			IDbContextFactory<UnityDbContext> dbContextFactory,
			RegistrationEventLog eventLog,
			IBetterStackLoggerController betterStackController,
			IScrutinyClient scrutinyClient,
			IPrivacyPolicyCache privacyPolicyCache)
		{
			_configService = configService;
			_dbContextFactory = dbContextFactory;
			_eventLog = eventLog;
			_betterStackController = betterStackController;
			_scrutinyClient = scrutinyClient;
			_privacyPolicyCache = privacyPolicyCache;

			// Seed the editable copy with whatever's currently in effect (either
			// the user-set value or the auto-default), and capture the auto-default
			// separately so we can show it as an Entry placeholder.
			DeviceLabel = _configService.DeviceLabel;
			DeviceLabelPlaceholder = _configService.DeviceLabel;

			// Seed the compliance email Entry with the persisted value (empty
			// on a fresh install — the placeholder in XAML invites the operator
			// to fill it in).
			ComplianceEmail = _configService.ComplianceEmail;

			// Load the cached privacy policy synchronously — Preferences
			// is a synchronous KVS so there's no async work to fire-and-
			// forget here. The page paints with the cache's contents
			// already populated; the live Refresh path runs only when
			// the user explicitly taps the button.
			LoadFromCache();
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
		// Compliance email: validate + save
		// =================================================================

		/// <summary>
		/// Validates and persists the typed-in compliance email address.
		/// An empty value is allowed and clears the configured recipient
		/// — callers downstream treat that as "no compliance recipient
		/// configured" and silently skip the send. A non-empty value is
		/// validated with <see cref="EmailAddressAttribute"/> (the same
		/// check used on member email fields elsewhere in the app); a
		/// failed validation surfaces a red status message and leaves
		/// the persisted value untouched.
		/// </summary>
		[RelayCommand]
		private void SaveComplianceEmail()
		{
			try
			{
				var trimmed = ComplianceEmail?.Trim() ?? string.Empty;

				if (trimmed.Length > 0 && !IsValidEmail(trimmed))
				{
					ShowComplianceEmailStatus(
						"That doesn't look like a valid email address.",
						isError: true);
					return;
				}

				_configService.SetComplianceEmail(trimmed);

				// Reflect the canonical (trimmed) value back to the bound
				// field so the Entry shows what was actually persisted.
				ComplianceEmail = trimmed;

				if (trimmed.Length == 0)
				{
					ShowComplianceEmailStatus(
						"Compliance email cleared.",
						isError: false);
					Logger.Information("Compliance email cleared from Settings");
				}
				else
				{
					ShowComplianceEmailStatus(
						$"Saved. Compliance service will use \"{trimmed}\".",
						isError: false);
					Logger.Information("Compliance email updated from Settings to {ComplianceEmail}", trimmed);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to save compliance email");
				ShowComplianceEmailStatus("Could not save compliance email — see logs.", isError: true);
			}
		}

		private void ShowComplianceEmailStatus(string message, bool isError)
		{
			ComplianceEmailStatusMessage = message;
			IsComplianceEmailStatusError = isError;
			IsComplianceEmailStatusVisible = true;
		}

		/// <summary>
		/// Same validator used on member email fields (see EditGroupViewModel /
		/// PositionEditViewModel). Wrapping in try/catch mirrors those call
		/// sites — <see cref="EmailAddressAttribute"/> can throw on
		/// pathological inputs, and a thrown validator should be treated as
		/// "not valid" rather than bubbling up to the user.
		/// </summary>
		private static bool IsValidEmail(string email)
		{
			try { return new EmailAddressAttribute().IsValid(email); }
			catch { return false; }
		}

		// =================================================================
		// Privacy policy cache: display + manual refresh
		// =================================================================

		/// <summary>
		/// Pulls the cached active policy out of <see cref="IPrivacyPolicyCache"/>
		/// and pushes it onto the bound display fields. Synchronous — Preferences
		/// is a synchronous KVS — so there's no in-flight state to worry about.
		/// Called on construction and after every successful Refresh.
		/// </summary>
		private void LoadFromCache()
		{
			var cached = _privacyPolicyCache.GetCached();
			if (cached is null)
			{
				HasActivePrivacyPolicy = false;
				PrivacyPolicyTitle = string.Empty;
				PrivacyPolicyVersion = string.Empty;
				PrivacyPolicyModified = string.Empty;
				PrivacyPolicyCachedAt = string.Empty;
				ShowPrivacyPolicyStatus(
					"No cached privacy policy on this device. Sync from Admin first.",
					isError: true);
				return;
			}

			PrivacyPolicyTitle = cached.Title;
			PrivacyPolicyVersion = string.IsNullOrWhiteSpace(cached.Version)
				? "(no version set)"
				: cached.Version;
			PrivacyPolicyModified = FormatModifiedRaw(cached.Modified);
			PrivacyPolicyCachedAt = FormatCachedAt(cached.CachedAt);
			HasActivePrivacyPolicy = true;

			// Don't auto-show a green "loaded from cache" status row on
			// first paint — there's nothing to acknowledge. The status
			// row is for transient feedback after an action (Refresh,
			// or a failed cache read). HidePrivacyPolicyStatus keeps
			// the page calm until the user does something.
			HidePrivacyPolicyStatus();
		}

		/// <summary>
		/// Manually re-fetches the active privacy policy from Scrutiny
		/// and updates the on-device cache. This is the same code path
		/// the sync stage runs, exposed as a Settings-page button so an
		/// operator can confirm or refresh the cached value without
		/// running a full data sync.
		///
		/// <para>The 404 / no-active-policy branch clears the cache
		/// (mirroring sync-stage behaviour) so the Settings display
		/// can't be left showing a policy that isn't published any
		/// more. Network failures preserve the cache.</para>
		/// </summary>
		[RelayCommand]
		private async Task RefreshFromScrutinyAsync()
		{
			try
			{
				IsPrivacyPolicyLoading = true;
				HidePrivacyPolicyStatus();

				var policy = await _scrutinyClient
					.GetActivePrivacyPolicyAsync()
					.ConfigureAwait(true);

				if (policy is null)
				{
					// Same call sequence the sync stage uses on 404 —
					// clear the cache and reload the display. The
					// reload will then surface the "no cached policy"
					// status message via LoadFromCache.
					_privacyPolicyCache.Clear();
					LoadFromCache();
					ShowPrivacyPolicyStatus(
						NoActivePrivacyPolicyException.DefaultMessage,
						isError: true);
					Logger.Warning("Manual refresh: Scrutiny reports no active privacy policy; cache cleared");
					return;
				}

				_privacyPolicyCache.Save(policy);
				LoadFromCache();
				ShowPrivacyPolicyStatus(
					$"Refreshed from Scrutiny (id {policy.Id.ToString(CultureInfo.InvariantCulture)}, version {policy.Version}).",
					isError: false);
				Logger.Information(
					"Manual refresh: cached active privacy policy {Id} version {Version}",
					policy.Id, policy.Version);
			}
			catch (InvalidOperationException ex)
			{
				// Thrown by ScrutinyClient when the Unity base URL
				// hasn't been configured yet. Cache is left untouched
				// — this is a config issue, not a stale-cache issue.
				Logger.Warning(ex, "Cannot refresh privacy policy: base URL not configured");
				ShowPrivacyPolicyStatus(
					"Configure the Unity base URL in API Settings first.",
					isError: true);
			}
			catch (HttpRequestException ex)
			{
				// Network failure — cache untouched, previous values
				// remain on screen. This is the offline case the user
				// flagged: the Settings page is still useful while
				// disconnected.
				Logger.Warning(ex, "Network error refreshing privacy policy from Scrutiny");
				ShowPrivacyPolicyStatus(
					$"Could not reach Scrutiny: {ex.Message}. Showing cached value.",
					isError: true);
			}
			catch (TaskCanceledException)
			{
				// HttpClient timeout. Same fallback as a network
				// failure — keep showing cached values.
				Logger.Warning("Timed out refreshing privacy policy from Scrutiny");
				ShowPrivacyPolicyStatus(
					"Timed out fetching the privacy policy. Showing cached value.",
					isError: true);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Unexpected error refreshing privacy policy");
				ShowPrivacyPolicyStatus(
					$"Could not refresh: {ex.Message}",
					isError: true);
			}
			finally
			{
				IsPrivacyPolicyLoading = false;
			}
		}

		/// <summary>
		/// Reformats the upstream "modified" ISO-8601 timestamp into
		/// something readable. Falls back to the raw string if parsing
		/// fails — better to show the upstream value verbatim than to
		/// swallow it on a server-side format change.
		/// </summary>
		private static string FormatModifiedRaw(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
				return string.Empty;

			if (DateTimeOffset.TryParse(
					raw,
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
					out var when))
			{
				// Local time so the timestamp matches what the operator
				// would see in their email client / WordPress admin.
				return when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
			}

			return raw;
		}

		/// <summary>
		/// Formats the local CachedAt timestamp for display. Shows both
		/// a friendly absolute time (so the operator can correlate with
		/// log entries) and a relative "N minutes ago" tail (so the
		/// staleness is obvious at a glance).
		/// </summary>
		private static string FormatCachedAt(DateTime cachedAtUtc)
		{
			if (cachedAtUtc == default)
				return string.Empty;

			var local = cachedAtUtc.ToLocalTime();
			var delta = DateTime.UtcNow - cachedAtUtc;

			string relative = delta switch
			{
				{ TotalSeconds: < 60 } => "just now",
				{ TotalMinutes: < 60 } => $"{(int)delta.TotalMinutes} min ago",
				{ TotalHours: < 24 } => $"{(int)delta.TotalHours} h ago",
				_ => $"{(int)delta.TotalDays} d ago",
			};

			return $"{local.ToString("g", CultureInfo.CurrentCulture)} ({relative})";
		}

		private void ShowPrivacyPolicyStatus(string message, bool isError)
		{
			PrivacyPolicyStatusMessage = message;
			IsPrivacyPolicyStatusError = isError;
			IsPrivacyPolicyStatusVisible = true;
		}

		private void HidePrivacyPolicyStatus()
		{
			IsPrivacyPolicyStatusVisible = false;
			PrivacyPolicyStatusMessage = string.Empty;
			IsPrivacyPolicyStatusError = false;
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

		/// <summary>
		/// Two-way bound to a Switch on SettingsPage. When on, the Edit
		/// Position page shows an "+ Add" button that lets operators create
		/// new holders for the position directly. When off, the button is
		/// hidden — holders can only be created via the Unity API. See
		/// <see cref="IConfigurationService.IsAddPositionHolderEnabled"/>.
		/// Defaults to off — fresh installs hide the button.
		/// Same "no local backing field" approach as the other toggles:
		/// Preferences is the source of truth.
		/// </summary>
		public bool IsAddPositionHolderEnabled
		{
			get => _configService.IsAddPositionHolderEnabled;
			set
			{
				if (_configService.IsAddPositionHolderEnabled == value) return;
				_configService.SetAddPositionHolderEnabled(value);
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Two-way bound to a Switch on SettingsPage. When on, every
		/// successful registration (group or position) queues a welcome /
		/// confirmation email to each affected member who has a
		/// <c>PersonalEmail</c> on file. Group registrations dedupe across
		/// active GSRs and any cascaded position holders, so no member is
		/// emailed twice from a single tap. See
		/// <see cref="IConfigurationService.IsWelcomeEmailOnRegistrationEnabled"/>.
		/// Defaults to off — fresh installs do not send any emails on
		/// registration until an operator turns this on.
		/// </summary>
		public bool IsWelcomeEmailOnRegistrationEnabled
		{
			get => _configService.IsWelcomeEmailOnRegistrationEnabled;
			set
			{
				if (_configService.IsWelcomeEmailOnRegistrationEnabled == value) return;
				_configService.SetWelcomeEmailOnRegistrationEnabled(value);
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
		/// Navigate to the combined API settings page. Replaces the
		/// previous two separate <c>NavigateToUnitySetting</c> and
		/// <c>NavigateToBetterStackSetting</c> commands — both Unity API and
		/// Better Stack configuration now live under a single page with an
		/// unsaved-changes guard.
		/// </summary>
		[RelayCommand]
		private async Task NavigateToApiSettings()
		{
			await ShowFeedback();
			await Shell.Current.GoToAsync(nameof(ApiSettingsPage));
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

		private static async Task ShowFeedback()
		{
			await Task.Delay(100);
		}
	}
}