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

	// Resolved once in SetupSerilog.
	private const string DefaultAppName = "Badi";
	private const string DefaultEnvironment = "Development";
	private static string _resolvedAppName = DefaultAppName;
	private static string _resolvedEnvironment = DefaultEnvironment;

	// Factory that produces a fresh base-logger configuration (file/console/debug
	// sinks + enrichers). Captured during SetupSerilog so BetterStackLoggerController
	// can rebuild the whole pipeline on demand when the user edits Better Stack
	// settings at runtime. Null until SetupSerilog runs.
	private static Func<LoggerConfiguration>? _baseLoggerFactory;

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

		builder.Services.AddSingleton<RegistrationEventLog>();

		// Add configuration service
		builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

		builder.Services.AddSingleton<SqlitePragmaInterceptor>();

		// ── Unity.Data: DbContext + Repositories ──────────────────────
		var unityDbPath = Path.Combine(FileSystem.AppDataDirectory, UNITY_DATABASE_NAME);
		//builder.Services.AddDbContext<UnityDbContext>((sp, options) =>
		//	options
		//		.UseSqlite($"Data Source={unityDbPath}")
		//		.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>()));

		builder.Services.AddDbContextFactory<UnityDbContext>((sp, options) =>
			options
				.UseSqlite($"Data Source={unityDbPath}")
				.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>()));

		//// Factory for ViewModels — each transient ViewModel creates its own
		//// short-lived DbContext, avoiding stale-entity tracking bleed between pages.
		//builder.Services.AddDbContextFactory<UnityDbContext>((sp, options) =>
		//	options
		//		.UseSqlite($"Data Source={unityDbPath}")
		//		.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>()),
		//	ServiceLifetime.Singleton);

		Log.Logger.Information("Unity Db {databasePath}", unityDbPath);

		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
		builder.Services.AddScoped<IMemberRepository, MemberRepository>();
		builder.Services.AddScoped<IPositionRepository, PositionRepository>();
		builder.Services.AddScoped<IIntergroupMeetingRepository, IntergroupMeetingRepository>();

		// --- HttpClient ---
		//
		// Two singletons:
		//
		//  1. The DEFAULT client (unkeyed) — platform-native handler. Used for
		//     Unity API traffic and anything else that goes through the same WAF.
		//     Some shared-hosting edge WAFs fingerprint TLS (JA3/JA4) and block
		//     .NET's managed SocketsHttpHandler while allowing requests from the
		//     platform's native HTTP stack (the same stack the system browser uses).
		//
		//       Windows       → WinHttpHandler         (schannel / WinHTTP)
		//       Android       → AndroidMessageHandler  (OkHttp)
		//       iOS / MacCat  → NSUrlSessionHandler    (NSURLSession)
		//       Other         → HttpClientHandler      (managed fallback)
		//
		//  2. A keyed "betterstack" client — SocketsHttpHandler with an aggressive
		//     PooledConnectionIdleTimeout. Better Stack isn't behind the fingerprinting
		//     WAF, so we don't need the native handler there, and WinHttpHandler has
		//     a known race (dotnet/runtime#22749, #121913) where a pooled keep-alive
		//     connection closed server-side produces WinHttpException 12152
		//     "The server returned an invalid or unrecognized response" on the next
		//     reuse. That fires on CloseAndFlush during app shutdown, because the
		//     sink has been idle during the edit session and its connection has
		//     usually timed out server-side by then. Shortening the client-side
		//     idle timeout below Better Stack's closes the pool first, avoiding
		//     the race entirely.
		builder.Services.AddSingleton<HttpClient>(_ => CreateHttpClient());
		builder.Services.AddKeyedSingleton<HttpClient>("betterstack", (_, _) => CreateBetterStackHttpClient());

		// Better Stack logger controller — rebuilds the Serilog pipeline on
		// demand when Better Stack settings change. Captures the base-logger
		// factory from SetupSerilog so every reconfigure composes a fresh
		// pipeline (base sinks + optional Better Stack sink) rather than
		// stacking sinks on top of the previous configuration. Singleton so
		// all callers share the serialisation lock inside the controller.
		builder.Services.AddSingleton<IBetterStackLoggerController>(sp =>
		{
			if (_baseLoggerFactory is null)
				throw new InvalidOperationException(
					"Serilog base-logger factory was not captured. SetupSerilog must run before the DI container is built.");

			var httpClient = sp.GetRequiredKeyedService<HttpClient>("betterstack");
			return new BetterStackLoggerController(_baseLoggerFactory, httpClient);
		});

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
		builder.Services.AddTransient<GroupEditPage>();
		builder.Services.AddTransient<GroupVerifyPage>();
		builder.Services.AddSingleton<DaySelectionPage>();
		builder.Services.AddSingleton<TypeSelectionPage>();
		builder.Services.AddTransient<GroupSelectionPage>();
		builder.Services.AddTransient<PositionEditPage>();
		builder.Services.AddTransient<PositionSelectionPage>();
		builder.Services.AddTransient<DatabaseBackupPage>();
		builder.Services.AddTransient<EmailStatusPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<UnitySettingsPage>();
		builder.Services.AddTransient<BetterStackSettingsPage>();
		builder.Services.AddTransient<AdminPage>();
		builder.Services.AddTransient<RegistrationOverviewPage>();

		// ── ViewModels ────────────────────────────────────────────────
		builder.Services.AddTransient<MailSettingsViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddTransient<GroupSelectionViewModel>();
		builder.Services.AddTransient<EditGroupViewModel>();
		builder.Services.AddTransient<VerifyGroupViewModel>();
		builder.Services.AddSingleton<TypeSelectionViewModel>();
		builder.Services.AddSingleton<DaySelectionViewModel>();
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

		// ── Attach Better Stack sink using user-saved settings ────────
		// SetupSerilog runs before DI is built, so it cannot read from
		// ConfigurationService. Once the container is available we ask the
		// IBetterStackLoggerController to layer the durable HTTP sink onto
		// the base pipeline. The same controller is injected into
		// BetterStackSettingsViewModel so runtime settings changes go through
		// the same code path and tear down the previous sink cleanly.
		//
		// ConfigurationService handles the dev/prod split itself — dev builds
		// read from the embedded devsettings.json, production builds read from
		// user-saved settings.
		using (var scope = mauiapp.Services.CreateScope())
		{
			var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
			var betterStackConfig = configService.GetBetterStackConfiguration();
			var controller = scope.ServiceProvider.GetRequiredService<IBetterStackLoggerController>();
			controller.Reconfigure(betterStackConfig);
		}
		return mauiapp;
	}

	private static void SetupSerilog(MauiAppBuilder builder)
	{
		var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(logPath);

		var appName = builder.Configuration["App:Name"] ?? DefaultAppName;
		var environment = builder.Configuration["App:Environment"] ?? DefaultEnvironment;

		// Persist for the Better Stack controller which rebuilds the pipeline
		// when settings change at runtime — it calls back into the factory below.
		_resolvedAppName = appName;
		_resolvedEnvironment = environment;

		// Capture the base-logger factory so the Better Stack controller can
		// rebuild a fresh pipeline on demand. We capture `builder.Configuration`
		// here because it won't be in scope once DI is built.
		var configRef = builder.Configuration;
		_baseLoggerFactory = () => BuildBaseLoggerConfiguration(configRef, logPath, appName, environment);

		Log.Logger = _baseLoggerFactory().CreateLogger();

		Log.Information("Application {AppName} v{Version} starting on {Platform}",
			appName, AppInfo.VersionString, DeviceInfo.Platform);
	}

	/// <summary>
	/// Builds a fresh <see cref="LoggerConfiguration"/> containing only the
	/// sinks that are fixed for the lifetime of the process — file, Debug, and
	/// (on desktop) console — plus all standard enrichers. The durable Better
	/// Stack sink is layered on separately by <see cref="BetterStackLoggerController"/>
	/// because it can be toggled/reconfigured at runtime from the settings page.
	///
	/// Returning a configuration rather than a built logger lets the controller
	/// chain <c>.WriteTo.DurableHttp...</c> before calling <c>CreateLogger()</c>,
	/// giving one unified pipeline rather than nested ones.
	/// </summary>
	private static LoggerConfiguration BuildBaseLoggerConfiguration(
		Microsoft.Extensions.Configuration.IConfiguration config,
		string logPath,
		string appName,
		string environment)
	{
		var cfg = new LoggerConfiguration()
			.ReadFrom.Configuration(config)
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
		cfg = cfg
			.WriteTo.File(Path.Combine(logPath, $"{appName.ToLower()}-debug-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 21)
			.WriteTo.Debug();

		// The Serilog console sink calls Console.set_ForegroundColor to apply its
		// colour theme, which throws PlatformNotSupportedException on Android and
		// iOS (System.Console has no ANSI terminal there). Every log event then
		// hits SelfLog with a stack trace, drowning real diagnostics.
		//
		// On mobile the Debug sink above already surfaces logs to the IDE's
		// output window, so Console adds nothing. Scope it to desktop only.
#if WINDOWS || MACCATALYST
		cfg = cfg.WriteTo.Console();
#endif
#else
        cfg = cfg.WriteTo.File(Path.Combine(logPath, $"{appName.ToLower()}-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            restrictedToMinimumLevel: LogEventLevel.Information);
#endif

		return cfg;
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		// Logging from a crash path must itself be crash-proof. If Log.Fatal
		// throws (e.g. the pipeline is already disposed, or an enricher faults
		// on this specific exception), we must not replace the original crash
		// with a logger crash. Belt and braces: Serilog already swallows most
		// internal errors to SelfLog, but this is a crash path — defence in
		// depth is essentially free.

		// .NET unhandled exceptions — background threads, async void, etc.
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			try
			{
				if (args.ExceptionObject is Exception ex)
					Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", args.IsTerminating);
				else
					Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", args.ExceptionObject);
			}
			catch { /* never throw from a crash handler */ }

			TryFlushLogs();
		};

		// Unobserved Task exceptions — app usually survives, so log but don't close
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			try { Log.Error(args.Exception, "Unobserved task exception"); }
			catch { /* never throw from a crash handler */ }
		};

#if ANDROID
		// Android-specific: Java-side unhandled exceptions bridged into .NET
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
		{
			try { Log.Fatal(args.Exception, "Unhandled Android exception"); }
			catch { /* never throw from a crash handler */ }

			TryFlushLogs();
		};
#endif
	}

	/// <summary>
	/// Close and flush all Serilog sinks with a bounded wait, never throwing.
	/// <c>Log.CloseAndFlush()</c> is synchronous and has no timeout; if the
	/// durable HTTP sink's final POST is slow or the endpoint is unreachable,
	/// it can block shutdown for up to <see cref="HttpClient.Timeout"/>. Anything
	/// still on disk after the cap will ship on the next process launch — that's
	/// the durable sink's entire purpose.
	/// </summary>
	internal static void TryFlushLogs(TimeSpan? timeout = null)
	{
		try
		{
			Task.Run(() => Log.CloseAndFlush()).Wait(timeout ?? TimeSpan.FromSeconds(5));
		}
		catch
		{
			// Never throw from a shutdown / crash path.
		}
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

	/// <summary>
	/// Creates the HttpClient used exclusively by the Better Stack log sink.
	/// Unlike <see cref="CreateHttpClient"/>, this uses the managed
	/// <see cref="SocketsHttpHandler"/> on every platform — Better Stack's
	/// ingest endpoint isn't behind the TLS-fingerprinting WAF that the
	/// platform-native handler exists to work around, and SocketsHttpHandler
	/// exposes the connection-pool knobs we need.
	///
	/// <para><b>PooledConnectionIdleTimeout = 30s</b> is the important one.
	/// Without it the client holds idle keep-alive connections until the server
	/// closes them, which on Windows with WinHttpHandler surfaces as a
	/// WinHttpException 12152 ("The server returned an invalid or unrecognized
	/// response") when the sink's periodic POST lands on a half-closed socket
	/// (dotnet/runtime#22749). The typical trigger is <c>Log.CloseAndFlush()</c>
	/// at shutdown after a long idle period. Closing client-side first makes
	/// the next request open a fresh connection.</para>
	///
	/// <para><b>PooledConnectionLifetime = 5min</b> additionally recycles
	/// connections so intermediaries that silently drop long-lived sockets
	/// (mobile NATs, corporate proxies) don't cause the same symptom.</para>
	/// </summary>
	private static HttpClient CreateBetterStackHttpClient()
	{
		var handler = new SocketsHttpHandler
		{
			PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};

		return new HttpClient(handler, disposeHandler: true)
		{
			// Tighter than the default app client — we'd rather fail fast and
			// let the durable sink retry from its on-disk buffer than block
			// shutdown behind a slow Better Stack response.
			Timeout = TimeSpan.FromSeconds(30),
		};
	}
}