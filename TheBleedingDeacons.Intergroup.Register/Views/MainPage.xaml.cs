using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class MainPage : ContentPage, IRegistrationWorkflow
{
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.RefreshMeetingStateAsync();
    }
}
