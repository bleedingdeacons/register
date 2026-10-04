using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class TypeSelectionPage : ContentPage, IRegistrationWorkflow
{
	public TypeSelectionPage(TypeSelectionViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}