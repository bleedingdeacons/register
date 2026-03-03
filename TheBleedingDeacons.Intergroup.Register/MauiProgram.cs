using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog;
using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Data.Data;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;
using TheBleedingDeacons.Unity.Data.Services;
using PopupNotificationService = TheBleedingDeacons.Intergroup.Register.Services.PopupNotificationService;

namespace TheBleedingDeacons.Intergroup.Register;

public static class MauiProgram
{
    public const string UNITY_DATABASE_NAME = "unity.db";
    public const string QUEUE_DATABASE_NAME = "queue.db";
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

        // Unity REST client — created on demand from config
        builder.Services.AddScoped<UnityRestSharp>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigurationService>();
            var config = configService.GetUnityConfiguration();
            if (!config.IsValid())
                throw new InvalidOperationException("Unity API is not configured.");
            return new UnityRestSharp(config.BaseUrl, config.ApiKey);
        });

        // UnitySyncService — fetches from API and replaces local SQLite data
        builder.Services.AddScoped<UnitySyncService>();

        // ── Queue DB (offline API call outbox) ────────────────────────
        var queueDbPath = Path.Combine(FileSystem.AppDataDirectory, QUEUE_DATABASE_NAME);
        builder.Services.AddDbContextFactory<QueueDbContext>(options =>
            options.UseSqlite($"Data Source={queueDbPath}"), ServiceLifetime.Scoped);

        Log.Logger.Information("Queue Db {databasePath}", queueDbPath);

        // ── Mail Database ─────────────────────────────────────────────
        var mailDbPath = Path.Combine(FileSystem.AppDataDirectory, MAIL_DATABASE_NAME);
        builder.Services.AddDbContextFactory<MailDbContext>(options =>
            options.UseSqlite($"Data Source={mailDbPath}"));

        // ── Register Services ─────────────────────────────────────────
        builder.Services.AddScoped<AttendanceService>();
        builder.Services.AddScoped<IAttendanceRegistration<Meeting>>(sp => sp.GetRequiredService<AttendanceService>());
        builder.Services.AddScoped<IAttendanceRegistration<Position>>(sp => sp.GetRequiredService<AttendanceService>());

        builder.Services.AddScoped<DataService>();
        builder.Services.AddScoped<SerializationService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<CacheService>();

        builder.Services.AddScoped<IPopupNotification, PopupNotificationService>();

        // Register Email Templates
        builder.Services.AddSingleton<IEmailTemplateService>(provider =>
        {
            return new EmailTemplateService(Assembly.GetExecutingAssembly(), "Templates");
        });

        // Register the mail service
        builder.Services.AddSingleton<IMailService>(provider =>
        {
            var dbContextFactory = provider.GetRequiredService<IDbContextFactory<MailDbContext>>();
            var configService = provider.GetRequiredService<IConfigurationService>();

            using var context = dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

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

        // Offline queue
        builder.Services.AddSingleton<IApiQueueService, ApiQueueService>();
        builder.Services.AddScoped<QueueingUnityApiService>();

        // ── Views ─────────────────────────────────────────────────────
        builder.Services.AddTransient<MailSettingsPage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<GroupEditPage>();
        builder.Services.AddTransient<GroupVerifyPage>();
        builder.Services.AddTransient<DaySelectionPage>();
        builder.Services.AddTransient<TypeSelectionPage>();
        builder.Services.AddTransient<MeetingSelectionPage>();
        builder.Services.AddTransient<ImportExportPage>();
        builder.Services.AddTransient<MeetingEditPage>();
        builder.Services.AddTransient<PositionEditPage>();
        builder.Services.AddTransient<PositionSelectionPage>();
        builder.Services.AddTransient<DatabaseBackupPage>();
        builder.Services.AddTransient<EmailStatusPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<UnitySettingsPage>();
        builder.Services.AddTransient<AdminPage>();

        // ── ViewModels ────────────────────────────────────────────────
        builder.Services.AddTransient<MailSettingsViewModel>();
        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<MeetingSelectionViewModel>();
        builder.Services.AddTransient<EditGroupViewModel>();
        builder.Services.AddTransient<VerifyGroupViewModel>();
        builder.Services.AddTransient<TypeSelectionViewModel>();
        builder.Services.AddTransient<DaySelectionViewModel>();
        builder.Services.AddTransient<ImportExportViewModel>();
        builder.Services.AddTransient<MeetingEditViewModel>();
        builder.Services.AddTransient<PositionSelectionViewModel>();
        builder.Services.AddTransient<PositionEditViewModel>();
        builder.Services.AddTransient<DatabaseBackupViewModel>();
        builder.Services.AddTransient<EmailStatusViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<UnitySettingsViewModel>();
        builder.Services.AddTransient<AdminViewModel>();

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

            var queueFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QueueDbContext>>();
            using var queueDb = queueFactory.CreateDbContext();
            queueDb.Database.EnsureCreated();

            System.Diagnostics.Debug.WriteLine("Unity and Queue databases initialized.");
        }

        // Start the queue service
        var queueService = mauiapp.Services.GetRequiredService<IApiQueueService>() as ApiQueueService;
        queueService?.Start();

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
}
