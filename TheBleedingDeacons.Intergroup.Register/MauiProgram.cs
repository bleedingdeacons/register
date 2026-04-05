using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
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

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
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

		// Ensure Serilog is flushed on unhandled / fatal errors
		RegisterGlobalExceptionHandlers();

		// Add configuration service
		builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

		// ── Unity.Data: DbContext + Repositories ──────────────────────
		var unityDbPath = Path.Combine(FileSystem.AppDataDirectory, UNITY_DATABASE_NAME);
		builder.Services.AddDbContext<UnityDbContext>(options =>
			options.UseSqlite($"Data Source={unityDbPath}"));

		Log.Logger.Information("Unity Db {databasePath}", unityDbPath);

		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
		builder.Services.AddScoped<IMemberRepository, MemberRepository>();
		builder.Services.AddScoped<IPositionRepository, PositionRepository>();
		builder.Services.AddScoped<IIntergroupMeetingRepository, IntergroupMeetingRepository>();

		// Unity REST client factory — always reads the latest credentials from config + SecureStorage.
		// Used by UnitySyncService so each sync call gets a fresh client.
		builder.Services.AddSingleton<Func<Task<UnityRestSharp>>>(sp =>
		{
			var configService = sp.GetRequiredService<IConfigurationService>();
			return async () =>
			{
				var config = await configService.LoadUnityConfigurationAsync();
				if (!config.IsValid())
					throw new InvalidOperationException("Unity API is not configured.");
				return new UnityRestSharp(config.BaseUrl, config.ApiKey);
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

		// Register Email Templates
		builder.Services.AddSingleton<IEmailTemplateService>(provider =>
		{
			return new EmailTemplateService(Assembly.GetExecutingAssembly(), "Templates");
		});

		// Register the mail service as scoped — same lifetime as AttendanceService
		// and DbContext, so event subscriptions are safe across the scope boundary.
		builder.Services.AddScoped<IMailService>(provider =>
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
		// ConfigurationService. We layer the BetterStack sink on here,
		// once the container (and SecureStorage) are available.
		using (var scope = mauiapp.Services.CreateScope())
		{
			var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
			var betterStackConfig = configService.GetBetterStackConfiguration();
			ReconfigureSerilogWithBetterStack(betterStackConfig);
		}

		return mauiapp;
	}

	private static void SetupSerilog(MauiAppBuilder builder)
	{
		var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(logPath);

		var appName = builder.Configuration["App:Name"] ?? "Badi";
		var environment = builder.Configuration["App:Environment"] ?? "Development";

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

	private static void ReconfigureSerilogWithBetterStack(BetterStackConfiguration betterStackConfig)
	{
		if (!betterStackConfig.IsValid())
		{
			Log.Information("Better Stack configuration is not set or invalid — skipping Better Stack sink");
			return;
		}

		// Mirror the same values used in SetupSerilog.
		var appName = "Badi";
		var environment = "Development";

		// Build the new logger into a local variable first. If CreateLogger()
		// or BetterStack() throws, Log.Logger keeps the original file/console/
		// debug sinks untouched. Only swap after success.
		try
		{
			var previousLogger = Log.Logger;

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
				.WriteTo.Logger(previousLogger)
				.WriteTo.BetterStack(
					sourceToken: betterStackConfig.SourceToken,
					betterStackEndpoint: betterStackConfig.Endpoint)
				.CreateLogger();

			// Only swap after the new logger is fully constructed.
			Log.Logger = newLogger;

			Log.Information("Better Stack sink attached to {Endpoint}", betterStackConfig.ToLogSafe().Endpoint);
		}
		catch (Exception ex)
		{
			// Log.Logger still points to the original — file/console/debug sinks intact.
			Log.Warning(ex, "Failed to attach Better Stack sink — continuing with existing sinks");
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
}