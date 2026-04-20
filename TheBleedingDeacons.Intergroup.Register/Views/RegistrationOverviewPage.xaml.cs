using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class RegistrationOverviewPage : ContentPage
{
	private readonly RegistrationOverviewViewModel _viewModel;

	public RegistrationOverviewPage(RegistrationOverviewViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	// Reload every time the page becomes visible so returning from the
	// Verify/Edit flow reflects the latest registered state. The ViewModel
	// guards against concurrent loads with its own IsBusy flag.
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}
}