using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

/// <summary>
/// Hosts the Freedom sign-in and the read-only Unity and Better Stack
/// connections. There is nothing to edit, so — unlike before credentials came
/// only from Freedom — there are no unsaved changes to guard on the way out.
///
/// <para>This file used to be called IntegrationsSettingsViewModel.cs while
/// holding this page's code-behind; it now has the name its class does.</para>
/// </summary>
public partial class ApiSettingsPage : ContentPage
{
    public ApiSettingsPage(ApiSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
