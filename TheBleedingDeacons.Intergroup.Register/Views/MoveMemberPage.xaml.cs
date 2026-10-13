using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class MoveMemberPage : ContentPage
{
	// Shell hands the memberId straight to the view-model, which is
	// IQueryAttributable through BaseViewModel. Don't forward it again
	// from here — see the note on EditPositionPage.
	public MoveMemberPage(MoveMemberViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
