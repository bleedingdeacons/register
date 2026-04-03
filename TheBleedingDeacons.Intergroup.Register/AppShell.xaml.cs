using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation        
        Routing.RegisterRoute(nameof(GroupEditPage), typeof(GroupEditPage));
        Routing.RegisterRoute(nameof(GroupVerifyPage), typeof(GroupVerifyPage));
        Routing.RegisterRoute(nameof(DaySelectionPage), typeof(DaySelectionPage));
        Routing.RegisterRoute(nameof(TypeSelectionPage), typeof(TypeSelectionPage));
        Routing.RegisterRoute(nameof(GroupSelectionPage), typeof(GroupSelectionPage));
        Routing.RegisterRoute(nameof(PositionSelectionPage), typeof(PositionSelectionPage));
        Routing.RegisterRoute(nameof(PositionEditPage), typeof(PositionEditPage));
        Routing.RegisterRoute(nameof(MailSettingsPage), typeof(MailSettingsPage));
        Routing.RegisterRoute(nameof(DatabaseBackupPage), typeof(DatabaseBackupPage));
        Routing.RegisterRoute(nameof(EmailStatusPage), typeof(EmailStatusPage));
        Routing.RegisterRoute(nameof(UnitySettingsPage), typeof(UnitySettingsPage));
        Routing.RegisterRoute(nameof(BetterStackSettingsPage), typeof(BetterStackSettingsPage));
        Routing.RegisterRoute(nameof(PositionVerifyPage), typeof(PositionVerifyPage));
    }
}