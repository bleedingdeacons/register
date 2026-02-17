using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog;
using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Client;
using PopupNotificationService = TheBleedingDeacons.Intergroup.Register.Services.PopupNotificationService;

namespace TheBleedingDeacons.Intergroup.Register;

public static class MauiProgram
{
    public const string APP_DATABASE_NAME = "register.db";
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
                //fonts.AddFont("Font Awesome 7 Brands-Regular-400.otf", "FontAwesome");
            });

        // Register logging service
        SetupSerilog(builder);

        // Add configuration service
        builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

        builder.Services.AddScoped<AttendanceService>();
        builder.Services.AddScoped<IAttendanceRegistration<Meeting>>(sp => sp.GetRequiredService<AttendanceService>());
        builder.Services.AddScoped<IAttendanceRegistration<Position>>(sp => sp.GetRequiredService<AttendanceService>());

        // App Database
        var appDbPath = Path.Combine(FileSystem.AppDataDirectory, APP_DATABASE_NAME);
        builder.Services.AddDbContext<RegisterContext>(options =>
            options.UseSqlite($"Data Source={appDbPath}"));

        Log.Logger.Information("Register Db {databasePath}", appDbPath);

        // Mail Database
        var mailDbPath = Path.Combine(FileSystem.AppDataDirectory, MAIL_DATABASE_NAME);
        builder.Services.AddDbContextFactory<MailDbContext>(options =>
            options.UseSqlite($"Data Source={mailDbPath}"));

        // Register Core Services
        builder.Services.AddScoped<DataService>();
        builder.Services.AddScoped<SerializationService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<CacheService>();
        builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
        builder.Services.AddScoped<IPositionRepository, PositionRepository>();
        builder.Services.AddScoped<IPopupNotification, PopupNotificationService>();

        // Register Email Templates
        builder.Services.AddSingleton<IEmailTemplateService>(provider =>
        {
            return new EmailTemplateService(Assembly.GetExecutingAssembly(), "Templates");
        });

        // Register the mail service with configuration
        builder.Services.AddSingleton<IMailService>(provider =>
        {
            var dbContextFactory = provider.GetRequiredService<IDbContextFactory<MailDbContext>>();
            var configService = provider.GetRequiredService<IConfigurationService>();

            // Ensure database is created
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



        // Register Unity API client and service
        builder.Services.AddSingleton<UnityRestSharp>(provider =>
        {
            var configService = provider.GetRequiredService<IConfigurationService>();
            var unityConfig = configService.GetUnityConfiguration();

            return new UnityRestSharp(
                unityConfig.BaseUrl.Length > 0 ? unityConfig.BaseUrl : "https://not-configured.local",
                unityConfig.ApiKey.Length > 0 ? unityConfig.ApiKey : "not-configured"
            );
        });
        builder.Services.AddScoped<IUnityApiService, UnityApiService>();

        // Views
        builder.Services.AddTransient<MailSettingsPage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<GsrEditPage>();
        builder.Services.AddTransient<DaySelectionPage>();
        builder.Services.AddTransient<TypeSelectionPage>();
        builder.Services.AddTransient<MeetingSelectionPage>();
        builder.Services.AddTransient<ImportExportPage>();
        builder.Services.AddTransient<GsrVerifyPage>();
        builder.Services.AddTransient<MeetingEditPage>();
        builder.Services.AddTransient<PositionEditPage>();
        builder.Services.AddTransient<PositionSelectionPage>();
        builder.Services.AddTransient<DatabaseBackupPage>();
        builder.Services.AddTransient<EmailStatusPage>();
        builder.Services.AddTransient<SettingsPage>();

        builder.Services.AddTransient<UnitySettingsPage>();

        // ViewModels        
        builder.Services.AddTransient<MailSettingsViewModel>();
        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<MeetingSelectionViewModel>();
        builder.Services.AddTransient<GsrEditViewModel>();
        builder.Services.AddTransient<TypeSelectionViewModel>();
        builder.Services.AddTransient<DaySelectionViewModel>();
        builder.Services.AddTransient<ImportExportViewModel>();
        builder.Services.AddTransient<GsrVerifyViewModel>();
        builder.Services.AddTransient<MeetingEditViewModel>();
        builder.Services.AddTransient<PositionSelectionViewModel>();
        builder.Services.AddTransient<PositionEditViewModel>();
        builder.Services.AddTransient<DatabaseBackupViewModel>();
        builder.Services.AddTransient<EmailStatusViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<UnitySettingsViewModel>();

#if DEBUG
        builder.Services.AddLogging();
        builder.Logging.AddDebug();
#endif


        var mauiapp = builder.Build();

        // Force database creation and load data synchronously
        using (var scope = mauiapp.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RegisterContext>();

            // Ensure database is created
            context.Database.EnsureCreated();

            // Load all data synchronously
            var meetings = context.Meetings.ToList();
            var positions = context.Positions.ToList();

            System.Diagnostics.Debug.WriteLine($"Loaded {meetings.Count} meetings and {positions.Count} positions.");
        }

        return mauiapp;
    }

    private static void SetupSerilog(MauiAppBuilder builder)
    {
        // Load appsettings.json

        var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
        Directory.CreateDirectory(logPath);

        // Get app configuration
        var appName = builder.Configuration["App:Name"] ?? "FareShare";
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

        //ConfigureLoki(config, builder.Configuration, appName, environment);
#endif

        Log.Logger = config.CreateLogger();

        // Log startup info
        Log.Information("Application {AppName} v{Version} starting on {Platform}",
            appName, AppInfo.VersionString, DeviceInfo.Platform);

    }
}