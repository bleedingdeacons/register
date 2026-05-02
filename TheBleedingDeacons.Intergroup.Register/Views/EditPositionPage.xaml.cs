using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class EditPositionPage : ContentPage, IQueryAttributable
{
	private readonly PositionEditViewModel _viewModel;

	public EditPositionPage(PositionEditViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (_viewModel is IQueryAttributable queryAttributable)
			queryAttributable.ApplyQueryAttributes(query);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		EditNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);

		// The Scrolled event only fires during scroll, so it won't prime the
		// chevron visibility on first layout when the content already overflows.
		// Listen for collection changes and refresh manually.
		_viewModel.DisplayedHolders.CollectionChanged += OnDisplayedHoldersChanged;
		RefreshDisplayedHoldersChevronsDeferred();
	}

	protected override void OnDisappearing()
	{
		_viewModel.DisplayedHolders.CollectionChanged -= OnDisplayedHoldersChanged;
		base.OnDisappearing();
	}

	private void OnDisplayedHoldersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		RefreshDisplayedHoldersChevronsDeferred();
	}

	private void RefreshDisplayedHoldersChevronsDeferred()
	{
		// Layout isn't guaranteed to be complete when the collection changes,
		// so push the visibility update to the next UI tick. At that point
		// FirstVisibleItemIndex / LastVisibleItemIndex reflect the laid-out
		// state we want to react to.
		Dispatcher.Dispatch(() =>
		{
			var c = _viewModel.DisplayedHolders.Count;
			if (c <= 1)
			{
				DisplayedHoldersScrollLeftButton.IsVisible = false;
				DisplayedHoldersScrollRightButton.IsVisible = false;
				return;
			}

			// Approximate content width from the known card width + spacing.
			// If the CollectionView itself hasn't been measured yet, Width
			// will be -1 and we bail out — Scrolled or the next layout pass
			// will take care of it.
			const double cardWidth = 560;
			const double spacing = 12;
			var contentWidth = (c * cardWidth) + ((c - 1) * spacing);
			var viewportWidth = DisplayedHoldersCollectionView.Width;

			if (viewportWidth <= 0)
			{
				// View not laid out yet — leave both hidden; the Scrolled
				// event or a subsequent pass will correct this.
				return;
			}

			var overflowsRight = contentWidth > viewportWidth;

			DisplayedHoldersScrollLeftButton.IsVisible = _displayedHoldersFirstVisibleIndex > 0;
			DisplayedHoldersScrollRightButton.IsVisible = overflowsRight;
		});
	}

	protected override bool OnBackButtonPressed()
	{
		if (BindingContext is PositionEditViewModel viewModel)
		{
			viewModel.DoneCommand.Execute(null);
			return true;
		}
		return base.OnBackButtonPressed();
	}

	// ── Horizontal scroll chevrons ────────────────────────────────────────
	// The Scrolled event fires continuously while the CollectionView scrolls,
	// including when the content-size vs viewport-size relationship first
	// settles after layout. We use FirstVisibleItemIndex / LastVisibleItemIndex
	// to detect "more content exists in that direction" and show the
	// corresponding chevron. A -1 index means the view isn't laid out yet or
	// has no items — we hide both chevrons in that case.
	//
	// CollectionView doesn't expose the current first-visible index as a
	// property — only via the Scrolled event args — so we cache the latest
	// values here. Click handlers read from the cache to compute the next
	// scroll target.
	//
	// IMPORTANT: XAML-wired event handlers must use non-nullable `object` for
	// the sender parameter. MAUI's XAML compiler does an exact signature match
	// and rejects `object?` with error XC0002.
	private int _displayedHoldersFirstVisibleIndex;
	private int _displayedHoldersLastVisibleIndex;

	private void DisplayedHoldersCollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
	{
		_displayedHoldersFirstVisibleIndex = e.FirstVisibleItemIndex;
		_displayedHoldersLastVisibleIndex = e.LastVisibleItemIndex;

		var count = _viewModel.DisplayedHolders.Count;
		if (count <= 1 || e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < 0)
		{
			DisplayedHoldersScrollLeftButton.IsVisible = false;
			DisplayedHoldersScrollRightButton.IsVisible = false;
			return;
		}

		DisplayedHoldersScrollLeftButton.IsVisible = e.FirstVisibleItemIndex > 0;
		DisplayedHoldersScrollRightButton.IsVisible = e.LastVisibleItemIndex < count - 1;
	}

	private void DisplayedHoldersScrollLeftButton_Clicked(object sender, EventArgs e)
	{
		// Step back from the FIRST visible card. Using LastVisibleItemIndex
		// here would skip cards when the viewport shows multiple at once.
		var target = Math.Max(0, _displayedHoldersFirstVisibleIndex - 1);
		DisplayedHoldersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	private void DisplayedHoldersScrollRightButton_Clicked(object sender, EventArgs e)
	{
		// Step forward from the LAST visible card. With SnapPointsAlignment=Center
		// on a viewport wide enough to show partial neighbours, FirstVisibleItemIndex
		// can stay at 0 even when card 1 is centred — using FirstVisibleItemIndex+1
		// would pin the user at card 1 forever. LastVisibleItemIndex+1 advances
		// past whatever's currently in view.
		var count = _viewModel.DisplayedHolders.Count;
		var target = Math.Min(count - 1, _displayedHoldersLastVisibleIndex + 1);
		DisplayedHoldersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}
}
