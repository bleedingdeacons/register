using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Inventory;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;
using TheBleedingDeacons.Unity.Intergroup.Services;
using PopupNotificationService = TheBleedingDeacons.Intergroup.Register.Services.PopupNotificationService;

namespace TheBleedingDeacons.Intergroup.Register;

public static class MauiProgram
{
	public const string UNITY_DATABASE_NAME = "unity.db";
	public const string MAIL_DATABASE_NAME = "emails.db";

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		// ── Load appsettings.json from embedded resource ──────────────
		// MAUI does not auto-load appsettings.json the way ASP.NET Core does.
		// The file is embedded in the assembly (see csproj <EmbeddedResource>)
		// and must be loaded explicitly so Serilog's ReadFrom.Configuration
		// and any builder.Configuration[...] lookups actually return values.
		var assembly = Assembly.GetExecutingAssembly();
		using (var stream = assembly.GetManifestResourceStream(
			"TheBleedingDeacons.Intergroup.Register.appsettings.json"))
		{
			if (stream is not null)
			{
				var jsonConfig = new ConfigurationBuilder()
					.AddJsonStream(stream)
					.Build();
				builder.Configuration.AddConfiguration(jsonConfig);
			}
			else
			{
				System.Diagnostics.Debug.WriteLine(
					"WARNING: appsettings.json embedded resource not found. " +
					"Available resources: " +
					string.Join(", ", assembly.GetManifestResourceNames()));
			}
		}

		// ── Layer devsettings.json on top, if present ─────────────────
		// devsettings.json is only embedded when the build was invoked
		// with UseDevCredentials=true (see csproj). When present it
		// overrides values from appsettings.json — most notably
		// App:Environment, which flips from "Production" to
		// "Development" so log entries are tagged correctly. Production
		// builds skip this section because the resource doesn't exist
		// in the assembly.
		using (var stream = assembly.GetManifestResourceStream(
			"TheBleedingDeacons.Intergroup.Register.devsettings.json"))
		{
			if (stream is not null)
			{
				var devConfig = new ConfigurationBuilder()
					.AddJsonStream(stream)
					.Build();
				builder.Configuration.AddConfiguration(devConfig);
			}
		}

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// ── Logging ───────────────────────────────────────────────────
		// Inventory (bleedingdeacons/inventory), shared with Link: the local
		// files, the enrichers, the crash handlers, ILogger<T> routed through
		// Serilog, and an ILogShipper that holds on disk until it is told
		// where to ship — see the end of this method. First, so what goes
		// wrong while the rest is built is on record.
		//
		// App:Name and App:Environment feed the enrichers and the log file
		// name. appsettings.json is git-ignored and CI writes a `{}`
		// placeholder, so a build without them is a real possibility; these
		// fallbacks are the ones this file has always had.
		var appName = builder.Configuration["App:Name"] ?? "Badi";
		builder.UseInventory(new InventoryMauiOptions
		{
			Application = appName,
			Environment = builder.Configuration["App:Environment"] ?? "Development",
#if DEBUG
			DeveloperSinks = true,
#endif
			// Not the app name, which has a space in it: adb logcat -s Register:V
			LogcatTag = "Register",
			AppVersion = AppVersion,
			Configure = cfg => cfg.ReadFrom.Configuration(builder.Configuration),
		});

		// Framework is its own property rather than folded into the message so
		// Better Stack can filter on it — the quickest way to tell one runtime
		// from another across a fleet of tablets.
		Log.Information(
			"Application {AppName} v{Version} (build {Build}, built {Built}) starting on {Platform} under {Framework}",
			appName, BuildInfo.Version, BuildInfo.Build, BuildInfo.BuildTimestamp,
			DeviceInfo.Platform, BuildInfo.Framework);

		// Freedom: the tablet's settings from the site instead of the build,
		// when this build names a Freedom site. See FreedomStartup.
		FreedomStartup.Register(builder);

		// Add configuration service. Given the Freedom client when there is
		// one, so a value Freedom holds is laid over the tablet's own.
		builder.Services.AddSingleton<IConfigurationService>(sp =>
			new ConfigurationService(sp.GetService<TheBleedingDeacons.Freedom.Client.FreedomClient>()));

		// Both logs in the one directory; see EventLogDirectory for where and why.
		var eventLogDirectory = EventLogDirectory.Resolve();
		builder.Services.AddSingleton(_ => new RegistrationEventLog(Path.Combine(eventLogDirectory, RegistrationEventLog.FileName)));
		builder.Services.AddSingleton(_ => new ComplianceEventLog(Path.Combine(eventLogDirectory, ComplianceEventLog.FileName)));

		// Before anything can create a member: until this runs the temporary
		// id counter lives in memory only. See TemporaryIdGenerator.
		TemporaryIdGenerator.Use(new PreferencesTemporaryIdStore());

		builder.Services.AddSingleton<SqlitePragmaInterceptor>();

		// ── Unity.Data: DbContext + Repositories ──────────────────────
		var unityDbPath = Path.Combine(FileSystem.AppDataDirectory, UNITY_DATABASE_NAME);
		builder.Services.AddDbContextFactory<UnityDbContext>((sp, options) =>
			options
				.UseSqlite($"Data Source={unityDbPath}")
				.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>()));

		Log.Logger.Information("Unity Db {DatabasePath}", unityDbPath);

		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
		builder.Services.AddScoped<IMemberRepository, MemberRepository>();
		builder.Services.AddScoped<IPositionRepository, PositionRepository>();
		builder.Services.AddScoped<IIntergroupMeetingRepository, IntergroupMeetingRepository>();

		// --- HttpClient ---
		//
		// The platform-native handler. Used for Unity API traffic and anything
		// else that goes through the same WAF. Some shared-hosting edge WAFs
		// fingerprint TLS (JA3/JA4) and block .NET's managed SocketsHttpHandler
		// while allowing requests from the platform's native HTTP stack (the
		// same stack the system browser uses).
		//
		//   Windows       → WinHttpHandler         (schannel / WinHTTP)
		//   Android       → AndroidMessageHandler  (OkHttp)
		//   iOS / MacCat  → NSUrlSessionHandler    (NSURLSession)
		//   Other         → HttpClientHandler      (managed fallback)
		//
		// Better Stack is not behind that WAF, and log shipping uses a client of
		// its own, tuned for a shipper that is idle most of the time — see
		// Inventory's BetterStackHttp.
		builder.Services.AddSingleton<HttpClient>(_ => CreateHttpClient());

		// Unity REST client factory — always reads the latest credentials from config + SecureStorage.
		// Used by UnitySyncService so each sync call gets a fresh client.
		builder.Services.AddSingleton<Func<Task<UnityRestSharp>>>(sp =>
		{
			var configService = sp.GetRequiredService<IConfigurationService>();
			var logger = sp.GetRequiredService<ILogger<UnityRestSharp>>();
			var platformClient = sp.GetRequiredService<HttpClient>();
			return async () =>
			{
				var config = await configService.LoadUnityConfigurationAsync();
				if (!config.IsValid())
					throw new InvalidOperationException("Unity API is not configured.");
				Log.Logger.Debug(
					"UnityRestSharp factory — BaseUrl: {BaseUrl}, ApiKey: {ApiKeyStatus}",
					config.BaseUrl,
					string.IsNullOrEmpty(config.ApiKey) ? "(not set)" : "***");
				return new UnityRestSharp(config.BaseUrl, config.ApiKey, platformClient, logger: logger);
			};
		});

		// Scrutiny REST client — read-only access to the privacy-policy
		// endpoints exposed by the Scrutiny WordPress plugin. Public on
		// the server side (no API key needed), but routed through the
		// same platform-native HttpClient as Unity so requests share the
		// OS TLS fingerprint at the edge WAF that fronts the same site.
		// Singleton: stateless beyond its dependencies, and the
		// configuration service it reads from caches its own results, so
		// there's no benefit to a per-call factory like UnityRestSharp's.
		builder.Services.AddSingleton<IScrutinyClient, ScrutinyClient>();

		// Privacy-policy cache — Preferences-backed, written by the
		// sync stage and read by ComplianceService (via the Verify*
		// view-models) and the Settings page. Singleton: stateless
		// beyond Preferences itself, which is process-wide and
		// thread-safe, so there's no concurrency concern in sharing
		// one instance across the app.
		builder.Services.AddSingleton<IPrivacyPolicyCache, PrivacyPolicyCache>();

		// UnitySyncService — fetches from API and replaces local SQLite data
		builder.Services.AddScoped<UnitySyncService>();

		// Snapshot + Reconciliation — local replica change tracking
		builder.Services.AddScoped<SnapshotService>();
		builder.Services.AddScoped<ReconciliationService>();

		// ── Mail Database ─────────────────────────────────────────────
		var mailDbPath = Path.Combine(FileSystem.AppDataDirectory, MAIL_DATABASE_NAME);
		builder.Services.AddDbContextFactory<MailDbContext>(options =>
			options.UseSqlite($"Data Source={mailDbPath}"));

		// ── Register Services ─────────────────────────────────────────
		builder.Services.AddScoped<AttendanceService>();
		builder.Services.AddScoped<IAttendanceRegistration<Group>>(sp => sp.GetRequiredService<AttendanceService>());
		builder.Services.AddScoped<IAttendanceRegistration<Position>>(sp => sp.GetRequiredService<AttendanceService>());

		// Compliance follows the same shape as AttendanceService — see
		// ComplianceService.cs for the rationale. Scoped lifetime keeps
		// it consistent with AttendanceService, even though the service
		// itself holds no per-request state; the lifetime matches the
		// DbContextFactory it depends on so all the Register-app
		// services share one disposal policy.
		builder.Services.AddScoped<ComplianceService>();
		builder.Services.AddScoped<IComplianceRegistration>(sp => sp.GetRequiredService<ComplianceService>());
		// The consent round the Verify pages run before registering. Transient,
		// because IComplianceRegistration above is scoped.
		builder.Services.AddTransient<ConsentRound>();

		builder.Services.AddScoped<DataService>();
		builder.Services.AddMemoryCache();
		builder.Services.AddSingleton<CacheService>();

		builder.Services.AddScoped<IPopupNotification, PopupNotificationService>();

		builder.Services.AddSingleton<IPhoneNumberService, PhoneNumberService>();

		// Register Email Templates
		builder.Services.AddSingleton<IEmailTemplateService>(provider =>
		{
			return new EmailTemplateService(Assembly.GetExecutingAssembly(), "Templates");
		});

		// Register the email service as singleton — EmailService owns a background
		// Timer for queue processing that must live for the entire app lifetime.
		// This is safe because the service only uses IDbContextFactory<MailDbContext>
		// (which is registered as singleton) rather than a scoped DbContext directly.
		// SMTP configuration changes are applied via UpdateConfigurationAsync().
		builder.Services.AddSingleton<IEmailService>(provider =>
		{
			var dbContextFactory = provider.GetRequiredService<IDbContextFactory<MailDbContext>>();
			var configService = provider.GetRequiredService<IConfigurationService>();

			var smtpConfig = configService.GetSmtpConfiguration();

			return new EmailService(
				dbContextFactory,
				() => Connectivity.Current.NetworkAccess == NetworkAccess.Internet,
				smtpConfig.Host,
				smtpConfig.Port,
				smtpConfig.Username,
				smtpConfig.Password,
				smtpConfig.EnableSsl
			);
		});

		// ── Views ─────────────────────────────────────────────────────
		builder.Services.AddTransient<MailSettingsPage>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddTransient<EditGroupPage>();
		builder.Services.AddTransient<VerifyGroupPage>();
		builder.Services.AddSingleton<DaySelectionPage>();
		builder.Services.AddSingleton<TypeSelectionPage>();
		// Singleton, like DaySelectionPage and TypeSelectionPage above: the
		// two list pages are each reached from exactly one place and never
		// appear twice on the navigation stack, so one instance can be
		// reused for the life of the app instead of rebuilding the page and
		// its CollectionView on every visit. See the view-model
		// registrations below for the reload behaviour this relies on.
		builder.Services.AddSingleton<GroupSelectionPage>();
		builder.Services.AddTransient<EditPositionPage>();
		builder.Services.AddSingleton<PositionSelectionPage>();
		builder.Services.AddTransient<DiagnosticDumpPage>();
		builder.Services.AddTransient<EmailStatusPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<ApiSettingsPage>();		
		builder.Services.AddTransient<AdminPage>();
		builder.Services.AddTransient<RegistrationOverviewPage>();

		// ── ViewModels ────────────────────────────────────────────────
		builder.Services.AddTransient<MailSettingsViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		// Singleton to match GroupSelectionPage. Its list still reloads on
		// every visit: DaySelectionViewModel hands over a freshly constructed
		// MeetingCriteria each time and MeetingCriteria is a plain class, so
		// reference inequality means OnCriteriaChanged fires even when the
		// day and type are unchanged. Nothing disposes this view-model, so
		// BaseViewModel's cancellation token stays live across visits.
		builder.Services.AddSingleton<GroupSelectionViewModel>();
		builder.Services.AddTransient<EditGroupViewModel>();
		builder.Services.AddTransient<VerifyGroupViewModel>();
		builder.Services.AddSingleton<TypeSelectionViewModel>();
		builder.Services.AddSingleton<DaySelectionViewModel>();
		// Singleton to match PositionSelectionPage. Its list still reloads on
		// every visit, because PositionSelectionPage.OnAppearing drives the
		// load and fires again each time the page is returned to.
		builder.Services.AddSingleton<PositionSelectionViewModel>();
		builder.Services.AddTransient<PositionEditViewModel>();
		builder.Services.AddTransient<DiagnosticDumpViewModel>();
		builder.Services.AddTransient<EmailStatusViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<ApiSettingsViewModel>();		
		builder.Services.AddTransient<AdminViewModel>();
		builder.Services.AddTransient<VerifyPositionViewModel>();
		builder.Services.AddTransient<VerifyPositionPage>();
		builder.Services.AddTransient<RegistrationOverviewViewModel>();

#if DEBUG
		builder.Services.AddLogging();
		builder.Logging.AddDebug();

		// Silence EF Core's per-command SQL logging in the Debug output window.
		// The Serilog override in appsettings.json handles ILogger<T> → Serilog,
		// but AddDebug writes directly to the MEL pipeline and needs its own filter.
		builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
		builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
#endif

		var mauiapp = builder.Build();

		// Ensure databases are created
		using (var scope = mauiapp.Services.CreateScope())
		{
			var unityDb = scope.ServiceProvider.GetRequiredService<UnityDbContext>();
			unityDb.Database.EnsureCreated();

			var mailDbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MailDbContext>>();
			using var mailDb = mailDbFactory.CreateDbContext();
			mailDb.Database.EnsureCreated();

			System.Diagnostics.Debug.WriteLine("Unity and Mail databases initialized.");
		}

		// ── Freedom: stored settings now, a sync in the background ────
		// Before the Better Stack sink below, which reads its endpoint and
		// token through ConfigurationService and so should see Freedom's.
		FreedomStartup.Start(mauiapp.Services);

		// ── Tell the log shipper where to ship ────────────────────────
		// UseInventory started it holding, because nothing could be read
		// before the container existed. Now it can: whatever was held since
		// launch ships under these settings, with its own timestamps. The
		// settings pages and FreedomStartup tell it again whenever the settings
		// change. ConfigurationService handles the dev/prod split and lays
		// Freedom's values over the tablet's own.
		mauiapp.Services.GetRequiredService<ILogShipper>()
			.Ship(mauiapp.Services.GetRequiredService<IConfigurationService>().GetBetterStackConfiguration());
		return mauiapp;
	}

	/// <summary>
	/// Creates an HttpClient backed by the platform's native HTTP handler.
	/// Native handlers use the OS TLS stack, which shares its JA3/JA4 fingerprint
	/// with the system browser and other OS-level HTTPS clients — making requests
	/// indistinguishable from "normal" traffic to reputation-based edge WAFs.
	/// </summary>
	private static HttpClient CreateHttpClient()
	{
		HttpMessageHandler handler;

#if WINDOWS
		handler = new System.Net.Http.WinHttpHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
			AutomaticRedirection = true,
		};
#elif ANDROID
		handler = new Xamarin.Android.Net.AndroidMessageHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};
#elif IOS || MACCATALYST
		// NSUrlSessionHandler honours the system's default decompression (gzip, br)
		// transparently; no AutomaticDecompression property is exposed.
		handler = new NSUrlSessionHandler();
#else
		handler = new HttpClientHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};
#endif

		return new HttpClient(handler, disposeHandler: true)
		{
			Timeout = TimeSpan.FromSeconds(100),
		};
	}

	public static string AppVersion()
	{
		if (DeviceInfo.Platform == DevicePlatform.WinUI)
		{
			return System.Diagnostics.FileVersionInfo
				.GetVersionInfo(System.Environment.ProcessPath!)
				.FileVersion ?? AppInfo.VersionString;
		}
		else
		{
			return AppInfo.VersionString;
		}
	}
}