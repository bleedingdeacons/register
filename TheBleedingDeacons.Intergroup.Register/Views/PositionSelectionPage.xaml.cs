using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TaskExtensions = TheBleedingDeacons.Intergroup.Register.Support.TaskExtensions;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class PositionSelectionPage : ContentPage
{
	private readonly PositionSelectionViewModel _viewModel;

	public PositionSelectionPage(PositionSelectionViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;

	}

	protected override void OnAppearing()
	{
		TaskExtensions.RunSafeFireAndForget(
			() => _viewModel.LoadDataAsync(),
			nameof(PositionSelectionPage) + "." + nameof(OnAppearing));

		base.OnAppearing();
	}

}