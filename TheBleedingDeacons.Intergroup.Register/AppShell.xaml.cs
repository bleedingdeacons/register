using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register;

public partial class AppShell : Shell
{
    public AppShell()
    {
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
        Routing.RegisterRoute(nameof(DatabaseBackupPage), typeof(DatabaseBackupPage));
        Routing.RegisterRoute(nameof(EmailStatusPage), typeof(EmailStatusPage));
        Routing.RegisterRoute(nameof(ApiSettingsPage), typeof(ApiSettingsPage));
        Routing.RegisterRoute(nameof(VerifyPositionPage), typeof(VerifyPositionPage));
    }
}
