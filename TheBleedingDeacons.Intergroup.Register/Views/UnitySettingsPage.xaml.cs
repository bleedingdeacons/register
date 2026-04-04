using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class UnitySettingsPage : ContentPage
{
    public UnitySettingsPage(UnitySettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
