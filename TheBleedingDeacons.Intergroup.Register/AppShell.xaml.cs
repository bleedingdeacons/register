using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register;

public partial class AppShell : Shell
{
    private static readonly ILogger Logger = AppLogger.ForContext<AppShell>();

    /// <summary>
    /// The operator's pages: Administration, Settings and everything opened
    /// from Settings. Each needs the tablet's screen code; see
    /// <see cref="OperatorLock"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> OperatorRoutes =
    [
        nameof(AdminPage),
        nameof(SettingsPage),
        nameof(MailSettingsPage),
        nameof(ApiSettingsPage),
        nameof(DiagnosticDumpPage),
        nameof(EmailStatusPage),
    ];

    private const string HomeRoute = "//" + nameof(MainPage);

    private readonly OperatorLock _lock;
    private readonly IScreenLockPrompt _prompt;

    // True while the screen-lock prompt is up. On older Android the prompt
    // is an activity of its own, so the app is "backgrounded" by asking.
    private bool _asking;

    public AppShell(OperatorLock operatorLock, IScreenLockPrompt prompt)
    {
        _lock = operatorLock;
        _prompt = prompt;

        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute(nameof(EditGroupPage), typeof(EditGroupPage));
        Routing.RegisterRoute(nameof(VerifyGroupPage), typeof(VerifyGroupPage));
        Routing.RegisterRoute(nameof(DaySelectionPage), typeof(DaySelectionPage));
        Routing.RegisterRoute(nameof(TypeSelectionPage), typeof(TypeSelectionPage));
        Routing.RegisterRoute(nameof(GroupSelectionPage), typeof(GroupSelectionPage));
        Routing.RegisterRoute(nameof(PositionSelectionPage), typeof(PositionSelectionPage));
        Routing.RegisterRoute(nameof(EditPositionPage), typeof(EditPositionPage));
        Routing.RegisterRoute(nameof(MailSettingsPage), typeof(MailSettingsPage));
        Routing.RegisterRoute(nameof(DiagnosticDumpPage), typeof(DiagnosticDumpPage));
        Routing.RegisterRoute(nameof(EmailStatusPage), typeof(EmailStatusPage));
        Routing.RegisterRoute(nameof(ApiSettingsPage), typeof(ApiSettingsPage));
        Routing.RegisterRoute(nameof(VerifyPositionPage), typeof(VerifyPositionPage));
        Routing.RegisterRoute(nameof(MoveMemberPage), typeof(MoveMemberPage));
    }

    /// <summary>The app went to the background.</summary>
    public void OnAppStopped()
    {
        if (!_asking)
            _lock.Leave();
    }

    /// <summary>
    /// The app came back. If that closed the window, or it ran out while an
    /// operator's page sat open, the page goes: what was on screen is no
    /// longer the operator's to see.
    /// </summary>
    public void OnAppResumed()
    {
        if (_asking)
            return;

        _lock.Return();

        if (!_lock.IsOpen && _lock.Guards(CurrentState?.Location?.OriginalString))
            GoHome("the app came back after the operator's window closed");
    }

    protected override async void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        var target = args.Target?.Location?.OriginalString;

        // Anywhere the room may go closes the operator's pages behind it.
        if (!_lock.Guards(target))
        {
            _lock.Close();
            return;
        }

        if (_lock.TryPass())
            return;

        if (!args.CanCancel)
        {
            Logger.Warning("Navigation to {Target} cannot be cancelled, so it was not asked for the screen code", target);
            return;
        }

        var deferral = args.GetDeferral();
        var result = ScreenLockResult.Declined;
        _asking = true;
        try
        {
            result = await _prompt.ConfirmAsync(
                "Operator access",
                "Enter this tablet's screen code to open Administration or Settings.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "The screen-lock prompt failed; {Target} stays closed", target);
        }
        finally
        {
            _asking = false;
        }

        if (result is ScreenLockResult.Confirmed or ScreenLockResult.NotOnThisPlatform)
        {
            _lock.Open();
        }
        else
        {
            args.Cancel();
        }

        deferral.Complete();

        if (result is ScreenLockResult.Confirmed or ScreenLockResult.NotOnThisPlatform)
            return;

        Logger.Information("Operator access to {Target} refused: {Result}", target, result);

        if (result == ScreenLockResult.NoScreenLock)
        {
            await DisplayAlertAsync(
                "Set a screen lock first",
                "Administration and Settings ask for this tablet's screen code, and it has none. " +
                "Set a PIN, pattern or password in Android's Settings, under Security, then try again.",
                "OK");
        }

        // Refused while already on an operator's page (the window ran out
        // there): leave it rather than keep showing it.
        if (_lock.Guards(CurrentState?.Location?.OriginalString))
            GoHome("the screen code was refused on an operator's page");
    }

    private void GoHome(string why)
    {
        Logger.Information("Leaving the operator's pages: {Why}", why);
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await GoToAsync(HomeRoute);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not leave the operator's pages");
            }
        });
    }
}
