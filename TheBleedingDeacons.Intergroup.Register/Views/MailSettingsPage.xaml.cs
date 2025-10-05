using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class MailSettingsPage : ContentPage
{
    public MailSettingsPage(MailSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}