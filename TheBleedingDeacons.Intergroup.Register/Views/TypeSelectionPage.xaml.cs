using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class TypeSelectionPage : ContentPage
{
	public TypeSelectionPage(TypeSelectionViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}