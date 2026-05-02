using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class VerifyPositionPage : ContentPage, IQueryAttributable
{
	private readonly VerifyPositionViewModel _viewModel;

	public VerifyPositionPage(VerifyPositionViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (_viewModel is IQueryAttributable queryAttributable)
		{
			queryAttributable.ApplyQueryAttributes(query);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		System.Diagnostics.Debug.WriteLine("=== VerifyPositionPage OnAppearing ===");
		System.Diagnostics.Debug.WriteLine($"Position is null: {_viewModel.Position == null}");
		System.Diagnostics.Debug.WriteLine($"CanRegister: {_viewModel.CanRegister}");

		// Prime scroll chevron visibility. See EditGroupPage for the rationale.
		_viewModel.ActiveHolders.CollectionChanged += OnActiveHoldersChanged;
		RefreshScrollChevronsDeferred();
	}

	protected override void OnDisappearing()
	{
		_viewModel.ActiveHolders.CollectionChanged -= OnActiveHoldersChanged;
		base.OnDisappearing();
	}

	// ─── Horizontal scroll chevrons ────────────────────────────────────
	// Same pattern as EditGroupPage / VerifyGroupPage.

	private int _activeHoldersFirstVisibleIndex;

	private void OnActiveHoldersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		RefreshScrollChevronsDeferred();
	}

	private void RefreshScrollChevronsDeferred()
	{
		Dispatcher.Dispatch(() =>
		{
			var count = _viewModel.ActiveHolders.Count;
			if (count <= 1)
			{
				ActiveHoldersScrollLeftButton.IsVisible = false;
				ActiveHoldersScrollRightButton.IsVisible = false;
				return;
			}

			const double cardWidth = 560;
			const double spacing = 12;
			var contentWidth = (count * cardWidth) + ((count - 1) * spacing);
			var viewportWidth = ActiveHoldersCollectionView.Width;

			if (viewportWidth <= 0) return;

			ActiveHoldersScrollLeftButton.IsVisible = _activeHoldersFirstVisibleIndex > 0;
			ActiveHoldersScrollRightButton.IsVisible = contentWidth > viewportWidth;
		});
	}

	private void ActiveHoldersCollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
	{
		_activeHoldersFirstVisibleIndex = e.FirstVisibleItemIndex;

		var count = _viewModel.ActiveHolders.Count;
		if (count <= 1 || e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < 0)
		{
			ActiveHoldersScrollLeftButton.IsVisible = false;
			ActiveHoldersScrollRightButton.IsVisible = false;
			return;
		}

		ActiveHoldersScrollLeftButton.IsVisible = e.FirstVisibleItemIndex > 0;
		ActiveHoldersScrollRightButton.IsVisible = e.LastVisibleItemIndex < count - 1;
	}

	private void ActiveHoldersScrollLeftButton_Clicked(object sender, EventArgs e)
	{
		var target = Math.Max(0, _activeHoldersFirstVisibleIndex - 1);
		ActiveHoldersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	private void ActiveHoldersScrollRightButton_Clicked(object sender, EventArgs e)
	{
		var count = _viewModel.ActiveHolders.Count;
		var target = Math.Min(count - 1, _activeHoldersFirstVisibleIndex + 1);
		ActiveHoldersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}
}