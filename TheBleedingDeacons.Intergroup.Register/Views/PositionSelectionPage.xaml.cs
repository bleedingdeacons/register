using TheBleedingDeacons.Intergroup.Register.ViewModels;

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
        _ = Task.Run(async () =>
        {
            await _viewModel.LoadDataAsync();
        });
        
        base.OnAppearing();
    }

}