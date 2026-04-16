using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Views;
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

	// Resolved once in SetupSerilog, reused by ReconfigureSerilogWithBetterStack
	// so both loggers carry identical Application / Environment properties.
	private const string DefaultAppName = "Badi";
	private const string DefaultEnvironment = "Development";
	private static string _resolvedAppName = DefaultAppName;
	private static string _resolvedEnvironment = DefaultEnvironment;

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

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Register logging service
		SetupSerilog(builder);

		// Bridge Serilog into Microsoft.Extensions.Logging so that
		// ILogger<T> resolved from DI flows through the Serilog pipeline.
		builder.Logging.AddSerilog();

		// Ensure Serilog is flushed on unhandled / fatal errors
		RegisterGlobalExceptionHandlers();

		// Add configuration service
		builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

		// ── Unity.Data: DbContext + Repositories ──────────────────────
		var unityDbPath = Path.Combine(FileSystem.AppDataDirectory, UNITY_DATABASE_NAME);
		builder.Services.AddDbContext<UnityDbContext>(options =>
			options.UseSqlite($"Data Source={unityDbPath}"));

		// Factory for ViewModels — each transient ViewModel creates its own
		// short-lived DbContext, avoiding stale-entity tracking bleed between pages.
		builder.Services.AddDbContextFactory<UnityDbContext>(options =>
			options.UseSqlite($"Data Source={unityDbPath}"), ServiceLifetime.Singleton);

		Log.Logger.Information("Unity Db {databasePath}", unityDbPath);

		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
		builder.Services.AddScoped<IMemberRepository, MemberRepository>();
		builder.Services.AddScoped<IPositionRepository, PositionRepository>();
		builder.Services.AddScoped<IIntergroupMeetingRepository, IntergroupMeetingRepository>();

		// --- HttpClient ---
		//
		// Register a single HttpClient built on the PLATFORM-NATIVE HTTP handler.
		// This is critical: some shared-hosting edge WAFs fingerprint TLS (JA3/JA4)
		// and block .NET's managed SocketsHttpHandler while allowing requests from
		// the platform's native HTTP stack (the same stack the system browser uses).
		//
		//   Windows       → WinHttpHandler         (schannel / WinHTTP)
		//   Android       → AndroidMessageHandler  (OkHttp)
		//   iOS / MacCat  → NSUrlSessionHandler    (NSURLSession)
		//   Other         → HttpClientHandler      (managed fallback)
		//
		// AutomaticDecompression is enabled where supported so every request sends
		// Accept-Encoding and the response is transparently decompressed.
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

		// Register the mail service as singleton — MailKitService owns a background
		// Timer for queue processing that must live for the entire app lifetime.
		// This is safe because the service only uses IDbContextFactory<MailDbContext>
		// (which is registered as singleton) rather than a scoped DbContext directly.
		// SMTP configuration changes are applied via UpdateConfigurationAsync().
		builder.Services.AddSingleton<IMailService>(provider =>
		{
			var dbContextFactory = provider.GetRequiredService<IDbContextFactory<MailDbContext>>();
			var configService = provider.GetRequiredService<IConfigurationService>();

			var smtpConfig = configService.GetSmtpConfiguration();

			return new MailKitService(
				dbContextFactory,
				smtpConfig.Host,
				smtpConfig.Port,
				smtpConfig.Username,
				smtpConfig.Password,
				smtpConfig.EnableSsl
			);
		});

		// ── Views ─────────────────────────────────────────────────────
		builder.Services.AddTransient<MailSettingsPage>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<GroupEditPage>();
		builder.Services.AddTransient<GroupVerifyPage>();
		builder.Services.AddTransient<DaySelectionPage>();
		builder.Services.AddTransient<TypeSelectionPage>();
		builder.Services.AddTransient<GroupSelectionPage>();
		builder.Services.AddTransient<PositionEditPage>();
		builder.Services.AddTransient<PositionSelectionPage>();
		builder.Services.AddTransient<DatabaseBackupPage>();
		builder.Services.AddTransient<EmailStatusPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<UnitySettingsPage>();
		builder.Services.AddTransient<BetterStackSettingsPage>();
		builder.Services.AddTransient<AdminPage>();

		// ── ViewModels ────────────────────────────────────────────────
		builder.Services.AddTransient<MailSettingsViewModel>();
		builder.Services.AddTransient<MainPageViewModel>();
		builder.Services.AddTransient<GroupSelectionViewModel>();
		builder.Services.AddTransient<EditGroupViewModel>();
		builder.Services.AddTransient<VerifyGroupViewModel>();
		builder.Services.AddTransient<TypeSelectionViewModel>();
		builder.Services.AddTransient<DaySelectionViewModel>();
		builder.Services.AddTransient<PositionSelectionViewModel>();
		builder.Services.AddTransient<PositionEditViewModel>();
		builder.Services.AddTransient<DatabaseBackupViewModel>();
		builder.Services.AddTransient<EmailStatusViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<UnitySettingsViewModel>();
		builder.Services.AddTransient<BetterStackSettingsViewModel>();
		builder.Services.AddTransient<AdminViewModel>();
		builder.Services.AddTransient<VerifyPositionViewModel>();
		builder.Services.AddTransient<PositionVerifyPage>();

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

		// ── Reconfigure Serilog with user-saved Better Stack settings ─
		// SetupSerilog runs before DI is built, so it cannot read from
		// ConfigurationService. We layer the durable HTTP sink on here,
		// once the container (and SecureStorage) are available.
		//
		// The HttpClient we resolve here is the same platform-native instance
		// used for Unity API calls — sharing the TLS stack ensures log uploads
		// aren't blocked by edge WAFs that allow the rest of the app through
		// (see CreateHttpClient for the JA3/JA4 rationale).
#if DEBUG
		ReconfigureSerilogWithBetterStack(
			new BetterStackConfiguration
			{
				Endpoint = "https://in.logs.betterstack.com",
				SourceToken = "goJkiJqUCb4qSJdFEpLx6hkw"
			},
			mauiapp.Services.GetRequiredService<HttpClient>());
#else
		using (var scope = mauiapp.Services.CreateScope())
		{
			var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
			var betterStackConfig = configService.GetBetterStackConfiguration();
			var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
			ReconfigureSerilogWithBetterStack(betterStackConfig, httpClient);
		}
#endif

		return mauiapp;
	}

	private static void SetupSerilog(MauiAppBuilder builder)
	{
		var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(logPath);

		var appName = builder.Configuration["App:Name"] ?? DefaultAppName;
		var environment = builder.Configuration["App:Environment"] ?? DefaultEnvironment;

		// Persist for ReconfigureSerilogWithBetterStack (which runs after DI is built).
		_resolvedAppName = appName;
		_resolvedEnvironment = environment;

		var config = new LoggerConfiguration()
			.ReadFrom.Configuration(builder.Configuration)
			.Enrich.WithProperty("Application", appName)
			.Enrich.WithProperty("Environment", environment)
			.Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
			.Enrich.WithProperty("PlatformVersion", DeviceInfo.VersionString)
			.Enrich.WithProperty("AppVersion", AppInfo.VersionString)
			.Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
			.Enrich.WithProperty("DeviceName", DeviceInfo.Name)
			.Enrich.WithProperty("ProcessId", Environment.ProcessId)
			.Enrich.WithProperty("MachineName", Environment.MachineName)
			.Enrich.With<ExceptionEnricher>();

#if DEBUG
		config
			.WriteTo.File(Path.Combine(logPath, $"{appName.ToLower()}-debug-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 21)
			.WriteTo.Console()
			.WriteTo.Debug();
#else
        config.WriteTo.File(Path.Combine(logPath, $"{appName.ToLower()}-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            restrictedToMinimumLevel: LogEventLevel.Information);
#endif

		Log.Logger = config.CreateLogger();

		Log.Information("Application {AppName} v{Version} starting on {Platform}",
			appName, AppInfo.VersionString, DeviceInfo.Platform);
	}

	private static void ReconfigureSerilogWithBetterStack(
		BetterStackConfiguration betterStackConfig,
		HttpClient httpClient)
	{
		if (!betterStackConfig.IsValid())
		{
			Log.Information("Better Stack configuration is not set or invalid — skipping Better Stack sink");
			return;
		}

		// Reuse the same values resolved during SetupSerilog.
		var appName = _resolvedAppName;
		var environment = _resolvedEnvironment;

		// ── Durable buffer path ───────────────────────────────────────
		// Sits alongside the existing text log files in AppDataDirectory.
		// • On Android / iOS this is private per-app storage, wiped on uninstall.
		// • It is backed by the platform's normal data dir, so buffered events
		//   survive process kills, reboots, and long offline stretches.
		// Serilog.Sinks.Http creates a bookmark file next to this base name
		// to track which buffered events have already been shipped.
		var bufferDir = Path.Combine(FileSystem.AppDataDirectory, "logs", "betterstack-buffer");
		Directory.CreateDirectory(bufferDir);
		var bufferBaseFileName = Path.Combine(bufferDir, "buffer");

		// Build the new logger into a local variable first. If CreateLogger()
		// or the durable sink setup throws, Log.Logger keeps the original
		// file/console/debug sinks untouched. Only swap after success.
		try
		{
			var previousLogger = Log.Logger;

			// ── Why DurableHttpUsingFileSizeRolledBuffers? ────────────
			// The durable sink writes each event to a rolling file *before*
			// attempting the HTTP POST. If the device is offline, the POST
			// just fails and is retried later — events stay on disk until
			// a batch is successfully acknowledged. Even a hard process
			// kill (OOM on Android, force-stop, device reboot) cannot lose
			// events that made it to the buffer file.
			var betterStackHttpClient = new BetterStackDurable.BetterStackHttpClient(
				betterStackConfig.SourceToken,
				httpClient);

			var newLogger = new LoggerConfiguration()
				.Enrich.WithProperty("Application", appName)
				.Enrich.WithProperty("Environment", environment)
				.Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
				.Enrich.WithProperty("PlatformVersion", DeviceInfo.VersionString)
				.Enrich.WithProperty("AppVersion", AppInfo.VersionString)
				.Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
				.Enrich.WithProperty("DeviceName", DeviceInfo.Name)
				.Enrich.WithProperty("ProcessId", Environment.ProcessId)
				.Enrich.WithProperty("MachineName", Environment.MachineName)
				.Enrich.With<ExceptionEnricher>()
				// Keep the existing file/console/debug sinks working exactly as before.
				.WriteTo.Logger(previousLogger)
				.WriteTo.DurableHttpUsingFileSizeRolledBuffers(
					requestUri:              betterStackConfig.Endpoint,
					bufferBaseFileName:      bufferBaseFileName,
					// Per-file roll limit. Smaller files mean faster recovery after a
					// crash (the sink replays the current file on startup). 8 MB gives
					// plenty of events per file without making replay slow.
					bufferFileSizeLimitBytes: 8L * 1024 * 1024,
					// Keep at most 16 rolled files (~128 MB). A phone that's been
					// offline for weeks will eventually hit this ceiling; older events
					// are discarded rather than filling the device. Tune upward if
					// long-offline retention matters more than disk footprint.
					retainedBufferFileCountLimit: 16,
					// Events per batch. Better Stack accepts up to 10 MiB per request;
					// 500 events with our enriched payloads stays well under that.
					logEventsInBatchLimit:   500,
					// Cap compressed batch size at 5 MiB to leave comfortable headroom
					// under Better Stack's 10 MiB request limit.
					batchSizeLimitBytes:     5L * 1024 * 1024,
					// How often to check the buffer for new events to ship.
					period:                  TimeSpan.FromSeconds(5),
					textFormatter:           new Serilog.Formatting.Json.JsonFormatter(renderMessage: true),
					batchFormatter:          new BetterStackDurable.BetterStackNdjsonBatchFormatter(),
					httpClient:              betterStackHttpClient)
				.CreateLogger();

			// Only swap after the new logger is fully constructed.
			Log.Logger = newLogger;

			// Enable Serilog's SelfLog during the first few seconds so any sink
			// setup errors are visible in Debug output — this is how you catch
			// typos in the endpoint, a revoked source token, or a malformed
			// buffer path without having to instrument anything further.
			Serilog.Debugging.SelfLog.Enable(msg => System.Diagnostics.Debug.WriteLine($"[Serilog] {msg}"));

			Log.Information(
				"Durable Better Stack sink attached to {Endpoint} (buffer: {BufferPath})",
				betterStackConfig.ToLogSafe().Endpoint,
				bufferBaseFileName);
		}
		catch (Exception ex)
		{
			// Log.Logger still points to the original — file/console/debug sinks intact.
			Log.Warning(ex, "Failed to attach durable Better Stack sink — continuing with existing sinks");
		}
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		// .NET unhandled exceptions — background threads, async void, etc.
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
				Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", args.IsTerminating);
			else
				Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", args.ExceptionObject);

			Log.CloseAndFlush();
		};

		// Unobserved Task exceptions — app usually survives, so log but don't close
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			Log.Error(args.Exception, "Unobserved task exception");
		};

#if ANDROID
		// Android-specific: Java-side unhandled exceptions bridged into .NET
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
		{
			Log.Fatal(args.Exception, "Unhandled Android exception");
			Log.CloseAndFlush();
		};
#endif
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
}


