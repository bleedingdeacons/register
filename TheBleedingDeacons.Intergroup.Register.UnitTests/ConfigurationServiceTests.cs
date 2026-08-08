using Microsoft.Maui.Devices;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.UnitTests.Fakes;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Covers the settings store: the feature toggles and their default-on /
/// default-off policy, the device label, and the credential round-trips.
///
/// <para>These tests build the app with <c>UseDevCredentials=false</c> (see the
/// test project's ProjectReference), so they exercise the production branches
/// — the ones that actually read Preferences and SecureStorage — rather than
/// the dev branches that short-circuit to an embedded devsettings.json.</para>
///
/// <para>Nothing here asserts on values inherited from the embedded
/// appsettings.json: that file is git-ignored and its contents differ between
/// a developer machine and CI. Every assertion is against a value the test
/// itself wrote.</para>
/// </summary>
public class ConfigurationServiceTests
{
	private static ConfigurationService Create(
		FakePreferences? prefs = null,
		TempFileSystem? files = null,
		FakeSecureStorage? secrets = null,
		FakeDeviceInfo? device = null) =>
		new(prefs ?? new FakePreferences(),
			files ?? new TempFileSystem(),
			secrets ?? new FakeSecureStorage(),
			device ?? new FakeDeviceInfo());

	// =====================================================================
	// Toggles that default ON
	// =====================================================================

	public static TheoryData<string, Func<ConfigurationService, bool>, Action<ConfigurationService, bool>> DefaultOnToggles => new()
	{
		{ "registration_log_enabled", c => c.IsRegistrationEventLogEnabled, (c, v) => c.SetRegistrationEventLogEnabled(v) },
		{ "auto_register_positions_on_group", c => c.IsAutoRegisterPositionsOnGroupEnabled, (c, v) => c.SetAutoRegisterPositionsOnGroupEnabled(v) },
		{ "compliance_log_enabled", c => c.IsComplianceEventLogEnabled, (c, v) => c.SetComplianceEventLogEnabled(v) },
	};

	[Theory]
	[MemberData(nameof(DefaultOnToggles))]
	public void DefaultOnToggle_IsEnabledOnAFreshInstall(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key; _ = write;

		Assert.True(read(Create()));
	}

	[Theory]
	[MemberData(nameof(DefaultOnToggles))]
	public void DefaultOnToggle_RoundTripsBothWays(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key;
		var service = Create();

		write(service, false);
		Assert.False(read(service));

		write(service, true);
		Assert.True(read(service));
	}

	[Theory]
	[MemberData(nameof(DefaultOnToggles))]
	public void DefaultOnToggle_FallsBackToEnabledOnAnUnparseableValue(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = write;
		var prefs = new FakePreferences();
		prefs.Seed(key, "not-a-bool");

		Assert.True(read(Create(prefs)));
	}

	[Theory]
	[MemberData(nameof(DefaultOnToggles))]
	public void DefaultOnToggle_FallsBackToEnabledWhenPreferencesThrow(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key; _ = write;
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };

		// Fail safe: losing the durability layer silently would be worse than
		// keeping it on when we cannot tell what the operator chose.
		Assert.True(read(Create(prefs)));
	}

	// =====================================================================
	// Toggles that default OFF
	// =====================================================================

	public static TheoryData<string, Func<ConfigurationService, bool>, Action<ConfigurationService, bool>> DefaultOffToggles => new()
	{
		{ "single_gsr_shortcut_enabled", c => c.IsSingleGsrShortcutEnabled, (c, v) => c.SetSingleGsrShortcutEnabled(v) },
		{ "add_position_holder_enabled", c => c.IsAddPositionHolderEnabled, (c, v) => c.SetAddPositionHolderEnabled(v) },
		{ "welcome_email_on_registration_enabled", c => c.IsWelcomeEmailOnRegistrationEnabled, (c, v) => c.SetWelcomeEmailOnRegistrationEnabled(v) },
	};

	[Theory]
	[MemberData(nameof(DefaultOffToggles))]
	public void DefaultOffToggle_IsDisabledOnAFreshInstall(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key; _ = write;

		Assert.False(read(Create()));
	}

	[Theory]
	[MemberData(nameof(DefaultOffToggles))]
	public void DefaultOffToggle_RoundTripsBothWays(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key;
		var service = Create();

		write(service, true);
		Assert.True(read(service));

		write(service, false);
		Assert.False(read(service));
	}

	[Theory]
	[MemberData(nameof(DefaultOffToggles))]
	public void DefaultOffToggle_FallsBackToDisabledOnAnUnparseableValue(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = write;
		var prefs = new FakePreferences();
		prefs.Seed(key, "not-a-bool");

		Assert.False(read(Create(prefs)));
	}

	[Theory]
	[MemberData(nameof(DefaultOffToggles))]
	public void DefaultOffToggle_FallsBackToDisabledWhenPreferencesThrow(
		string key, Func<ConfigurationService, bool> read, Action<ConfigurationService, bool> write)
	{
		_ = key; _ = write;
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };

		// No-surprise-emails / no-surprise-UI invariant.
		Assert.False(read(Create(prefs)));
	}

	// =====================================================================
	// Compliance-acceptance-email toggle — the odd one out
	// =====================================================================

	[Fact]
	public void ComplianceAcceptanceEmail_IsEnabledOnAFreshInstall()
	{
		Assert.True(Create().IsComplianceAcceptanceEmailEnabled);
	}

	[Fact]
	public void ComplianceAcceptanceEmail_RoundTripsBothWays()
	{
		var service = Create();

		service.SetComplianceAcceptanceEmailEnabled(false);
		Assert.False(service.IsComplianceAcceptanceEmailEnabled);

		service.SetComplianceAcceptanceEmailEnabled(true);
		Assert.True(service.IsComplianceAcceptanceEmailEnabled);
	}

	[Fact]
	public void ComplianceAcceptanceEmail_DisagreesWithItsSiblingsOnTheFailurePaths()
	{
		// Pinning current behaviour, which is inconsistent and probably not
		// intended — see the note in TESTPLAN.md §4.
		//
		// Unset means ENABLED (above), but an unparseable value or a broken
		// prefs store means DISABLED. Every other default-on toggle stays on
		// in both of those cases. The XML doc on the property contradicts
		// itself too: it says the default is true, then explains that fresh
		// installs "do not send ... until an operator opts in".
		var corrupt = new FakePreferences();
		corrupt.Seed("compliance_acceptance_email_enabled", "not-a-bool");
		Assert.False(Create(corrupt).IsComplianceAcceptanceEmailEnabled);

		var broken = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };
		Assert.False(Create(broken).IsComplianceAcceptanceEmailEnabled);
	}

	// =====================================================================
	// Compliance email address
	// =====================================================================

	[Fact]
	public void ComplianceEmail_IsEmptyWhenUnconfigured()
	{
		Assert.Equal(string.Empty, Create().ComplianceEmail);
	}

	[Fact]
	public void SetComplianceEmail_TrimsBeforeStoring()
	{
		var service = Create();

		service.SetComplianceEmail("  compliance@example.org  ");

		Assert.Equal("compliance@example.org", service.ComplianceEmail);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void SetComplianceEmail_ClearsTheRecipientOnBlankInput(string? blank)
	{
		var service = Create();
		service.SetComplianceEmail("compliance@example.org");

		service.SetComplianceEmail(blank);

		Assert.Equal(string.Empty, service.ComplianceEmail);
	}

	[Fact]
	public void ComplianceEmail_IsEmptyWhenPreferencesThrow()
	{
		// Skip the send rather than throw mid-acceptance.
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };

		Assert.Equal(string.Empty, Create(prefs).ComplianceEmail);
	}

	// =====================================================================
	// Device label
	// =====================================================================

	[Fact]
	public void DeviceLabel_PrefersTheOperatorSetValue()
	{
		var service = Create();
		service.SetDeviceLabel("  Front Desk Tablet  ");

		Assert.Equal("Front Desk Tablet", service.DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_SynthesisesFromDeviceInfoWhenUnset()
	{
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.Android,
			Manufacturer = "Google",
			Model = "Pixel 8",
			VersionString = "15",
		};

		Assert.Equal("Google Pixel 8 (Android 15)", Create(device: device).DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_DoesNotRepeatAManufacturerAlreadyInTheModel()
	{
		// Samsung reports "Samsung SM-G991B" as the model; "Samsung Samsung
		// SM-G991B" would be the naive result.
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.Android,
			Manufacturer = "Samsung",
			Model = "Samsung SM-G991B",
			VersionString = "14",
		};

		Assert.Equal("Samsung SM-G991B (Android 14)", Create(device: device).DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_OmitsTheVersionWhenTheDeviceDoesNotReportOne()
	{
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.iOS,
			Manufacturer = "Apple",
			Model = "iPad",
			VersionString = "",
		};

		Assert.Equal("Apple iPad (iOS)", Create(device: device).DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_FallsBackToAPlaceholderWhenTheHardwareIsUnknown()
	{
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.Android,
			Manufacturer = "",
			Model = "",
			VersionString = "15",
		};

		Assert.Equal("Device (Android 15)", Create(device: device).DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_UsesTheMachineNameOnDesktop()
	{
		// On desktop the OS host name is what the operator already recognises,
		// so it wins over the manufacturer/model synthesis.
		var device = new FakeDeviceInfo { Platform = DevicePlatform.WinUI };

		Assert.Equal(Environment.MachineName, Create(device: device).DeviceLabel);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("   ")]
	public void SetDeviceLabel_ClearsBackToTheAutoDefault(string? blank)
	{
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.Android,
			Manufacturer = "Google",
			Model = "Pixel 8",
			VersionString = "15",
		};
		var service = Create(device: device);
		service.SetDeviceLabel("Front Desk Tablet");

		service.SetDeviceLabel(blank);

		Assert.Equal("Google Pixel 8 (Android 15)", service.DeviceLabel);
	}

	[Fact]
	public void DeviceLabel_FallsBackToTheAutoDefaultWhenPreferencesThrow()
	{
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };
		var device = new FakeDeviceInfo
		{
			Platform = DevicePlatform.Android,
			Manufacturer = "Google",
			Model = "Pixel 8",
			VersionString = "15",
		};

		Assert.Equal("Google Pixel 8 (Android 15)", Create(prefs, device: device).DeviceLabel);
	}

	// =====================================================================
	// Active intergroup meeting
	// =====================================================================

	[Fact]
	public async Task ActiveIntergroupMeeting_RoundTripsThroughPreferences()
	{
		using var files = new TempFileSystem();
		var prefs = new FakePreferences();
		var service = Create(prefs, files);

		await service.SaveActiveIntergroupMeetingAsync(7);

		var loaded = await service.LoadUnityConfigurationAsync();
		Assert.Equal(7, loaded.ActiveIntergroupMeetingId);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-3)]
	public async Task ActiveIntergroupMeeting_IsClearedByNonPositiveValues(int? value)
	{
		using var files = new TempFileSystem();
		var prefs = new FakePreferences();
		var service = Create(prefs, files);
		await service.SaveActiveIntergroupMeetingAsync(7);

		await service.SaveActiveIntergroupMeetingAsync(value);

		Assert.False(prefs.ContainsKey("unity_active_meeting_id"));
		var loaded = await service.LoadUnityConfigurationAsync();
		Assert.Null(loaded.ActiveIntergroupMeetingId);
	}

	[Theory]
	[InlineData("not-a-number")]
	[InlineData("0")]
	[InlineData("-1")]
	public async Task ActiveIntergroupMeeting_IgnoresAnUnusableStoredValue(string stored)
	{
		using var files = new TempFileSystem();
		var prefs = new FakePreferences();
		prefs.Seed("unity_active_meeting_id", stored);

		var loaded = await Create(prefs, files).LoadUnityConfigurationAsync();

		Assert.Null(loaded.ActiveIntergroupMeetingId);
	}

	// =====================================================================
	// Credential round-trips
	// =====================================================================

	[Fact]
	public async Task UnityConfiguration_RoundTripsUrlToDiskAndKeyToSecureStorage()
	{
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage();
		var service = Create(files: files, secrets: secrets);

		await service.SaveUnityConfigurationAsync(new UnityConfiguration
		{
			BaseUrl = "https://unity.example.org",
			ApiKey = "unity-secret",
		});

		var loaded = await service.LoadUnityConfigurationAsync();

		Assert.Equal("https://unity.example.org", loaded.BaseUrl);
		Assert.Equal("unity-secret", loaded.ApiKey);

		// The key belongs in SecureStorage, never in the settings file.
		Assert.Equal("unity-secret", await secrets.GetAsync("unity_api_key"));
		var onDisk = await File.ReadAllTextAsync(Path.Combine(files.AppDataDirectory, "unitysettings.json"));
		Assert.DoesNotContain("unity-secret", onDisk, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UnityConfiguration_LoadsEmptyWhenNothingHasBeenSaved()
	{
		using var files = new TempFileSystem();

		var loaded = await Create(files: files).LoadUnityConfigurationAsync();

		Assert.Equal(string.Empty, loaded.BaseUrl);
		Assert.Equal(string.Empty, loaded.ApiKey);
		Assert.False(loaded.IsValid());
	}

	[Fact]
	public async Task UnityConfiguration_TreatsAnUnavailableKeystoreAsNoKey()
	{
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage { FailWith = new InvalidOperationException("keystore broken") };

		var loaded = await Create(files: files, secrets: secrets).LoadUnityConfigurationAsync();

		Assert.Equal(string.Empty, loaded.ApiKey);
	}

	[Fact]
	public async Task BetterStackConfiguration_RoundTripsEndpointToDiskAndTokenToSecureStorage()
	{
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage();
		var service = Create(files: files, secrets: secrets);

		await service.SaveBetterStackConfigurationAsync(new BetterStackConfiguration
		{
			Endpoint = "https://in.logs.example.org",
			SourceToken = "bs-secret",
		});

		var loaded = await service.LoadBetterStackConfigurationAsync();

		Assert.Equal("https://in.logs.example.org", loaded.Endpoint);
		Assert.Equal("bs-secret", loaded.SourceToken);

		var onDisk = await File.ReadAllTextAsync(Path.Combine(files.AppDataDirectory, "betterstacksettings.json"));
		Assert.DoesNotContain("bs-secret", onDisk, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SmtpConfiguration_SurvivesARestart()
	{
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage();

		await Create(files: files, secrets: secrets).SaveSmtpConfigurationAsync(new SmtpConfiguration
		{
			Host = "smtp.example.org",
			Port = 2525,
			Username = "postmaster@example.org",
			Password = "smtp-secret",
			FromDisplayName = "Intergroup Register",
			EnableSsl = false,
			TimeoutSeconds = 45,
		});

		// New instance over the same storage — as after an app restart. The
		// non-secret fields are re-read from mailsettings.json at construction.
		var restarted = await Create(files: files, secrets: secrets).LoadSmtpConfigurationAsync();

		Assert.Equal("smtp.example.org", restarted.Host);
		Assert.Equal(2525, restarted.Port);
		Assert.Equal("postmaster@example.org", restarted.Username);
		Assert.Equal("Intergroup Register", restarted.FromDisplayName);
		Assert.False(restarted.EnableSsl);
		Assert.Equal(45, restarted.TimeoutSeconds);
		Assert.Equal("smtp-secret", restarted.Password);
	}

	[Fact]
	public async Task SmtpConfiguration_KeepsThePasswordOutOfTheSettingsFile()
	{
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage();
		var service = Create(files: files, secrets: secrets);

		await service.SaveSmtpConfigurationAsync(new SmtpConfiguration
		{
			Host = "smtp.example.org",
			Password = "smtp-secret",
		});

		var onDisk = await File.ReadAllTextAsync(Path.Combine(files.AppDataDirectory, "mailsettings.json"));
		Assert.DoesNotContain("smtp-secret", onDisk, StringComparison.Ordinal);
		Assert.Equal("smtp-secret", await secrets.GetAsync("smtp_password"));
	}

	[Fact]
	public async Task SmtpConfiguration_ReloadingOnTheSameInstanceLosesTheJustSavedValues()
	{
		// Pinning a defect, not endorsing it — see TESTPLAN.md §4.
		//
		// The IConfiguration that LoadSmtpConfigurationAsync binds from is
		// built once in the constructor, so it never sees a mailsettings.json
		// written later in the same session. Worse, Load overwrites the cache
		// that Save had just populated with the correct values, so a
		// save-then-reload sequence (which is what the Settings page does)
		// leaves the service serving the pre-save host with the post-save
		// password.
		using var files = new TempFileSystem();
		var secrets = new FakeSecureStorage();
		var service = Create(files: files, secrets: secrets);

		await service.SaveSmtpConfigurationAsync(new SmtpConfiguration
		{
			Host = "smtp.example.org",
			Password = "smtp-secret",
		});

		Assert.Equal("smtp.example.org", service.GetSmtpConfiguration().Host);

		var reloaded = await service.LoadSmtpConfigurationAsync();

		Assert.Equal(string.Empty, reloaded.Host);      // ← should be smtp.example.org
		Assert.Equal("smtp-secret", reloaded.Password); // password comes from SecureStorage, so it is current
		Assert.Equal(string.Empty, service.GetSmtpConfiguration().Host); // cache clobbered
	}

	// =====================================================================
	// Construction
	// =====================================================================

	[Fact]
	public void Constructor_RejectsEachMissingPlatformService()
	{
		using var files = new TempFileSystem();

		Assert.Throws<ArgumentNullException>(() =>
			new ConfigurationService(null!, files, new FakeSecureStorage(), new FakeDeviceInfo()));
		Assert.Throws<ArgumentNullException>(() =>
			new ConfigurationService(new FakePreferences(), null!, new FakeSecureStorage(), new FakeDeviceInfo()));
		Assert.Throws<ArgumentNullException>(() =>
			new ConfigurationService(new FakePreferences(), files, null!, new FakeDeviceInfo()));
		Assert.Throws<ArgumentNullException>(() =>
			new ConfigurationService(new FakePreferences(), files, new FakeSecureStorage(), null!));
	}
}
