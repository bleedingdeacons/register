using TheBleedingDeacons.Intergroup.Register.Support;
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
		base.OnAppearing();

		// Invoke the load directly on the UI thread (which is where
		// OnAppearing already runs) — don't Task.Run it. The previous
		// RunSafeFireAndForget put the load on a thread-pool thread, so
		// Positions.Clear() raised CollectionChanged from there and the
		// bound CollectionView couldn't touch its views off the UI thread
		// on Android. That crashed on the second appearance, when coming
		// back from VerifyPositionPage found the collection populated.
		// LoadDataAsync awaits its DB read internally, so the UI thread is
		// released for the duration. Mirrors EmailStatusPage.OnAppearing.
		_viewModel.LoadDataAsync()
			.SafeFireAndForget(nameof(PositionSelectionPage) + "." + nameof(OnAppearing));
	}

}