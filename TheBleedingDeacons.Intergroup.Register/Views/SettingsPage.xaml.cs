using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}