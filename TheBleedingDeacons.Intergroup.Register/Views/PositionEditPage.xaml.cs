using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class PositionEditPage : ContentPage
{

	private readonly PositionEditViewModel _viewModel;


    public PositionEditPage(PositionEditViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = _viewModel = viewModel;

	}

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Configure keyboard for name entry with CapitalizeWord
        EditMemberNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);
    }
}