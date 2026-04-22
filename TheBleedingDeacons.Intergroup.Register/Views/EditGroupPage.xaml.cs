using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class EditGroupPage : ContentPage, IQueryAttributable
{
	private readonly EditGroupViewModel _viewModel;

	public EditGroupPage(EditGroupViewModel viewModel)
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
		_viewModel.ActiveMembers.CollectionChanged += OnActiveMembersChanged;
		_viewModel.PendingRemovals.CollectionChanged += OnPendingRemovalsChanged;
		RefreshActiveMembersChevronsDeferred();
		RefreshPendingRemovalsChevronsDeferred();
	}

	protected override void OnDisappearing()
	{
		_viewModel.ActiveMembers.CollectionChanged -= OnActiveMembersChanged;
		_viewModel.PendingRemovals.CollectionChanged -= OnPendingRemovalsChanged;
		base.OnDisappearing();
	}

	private void OnActiveMembersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		RefreshActiveMembersChevronsDeferred();
	}

	private void OnPendingRemovalsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		RefreshPendingRemovalsChevronsDeferred();
	}

	private void RefreshActiveMembersChevronsDeferred()
	{
		RefreshChevronsDeferred(
			count: () => _viewModel.ActiveMembers.Count,
			collectionView: ActiveMembersCollectionView,
			firstVisibleIndex: _activeMembersFirstVisibleIndex,
			leftButton: ActiveMembersScrollLeftButton,
			rightButton: ActiveMembersScrollRightButton);
	}

	private void RefreshPendingRemovalsChevronsDeferred()
	{
		RefreshChevronsDeferred(
			count: () => _viewModel.PendingRemovals.Count,
			collectionView: PendingRemovalsCollectionView,
			firstVisibleIndex: _pendingRemovalsFirstVisibleIndex,
			leftButton: PendingRemovalsScrollLeftButton,
			rightButton: PendingRemovalsScrollRightButton);
	}

	private void RefreshChevronsDeferred(
		Func<int> count,
		CollectionView collectionView,
		int firstVisibleIndex,
		Button leftButton,
		Button rightButton)
	{
		// Layout isn't guaranteed to be complete when the collection changes,
		// so push the visibility update to the next UI tick. At that point
		// FirstVisibleItemIndex / LastVisibleItemIndex reflect the laid-out
		// state we want to react to.
		Dispatcher.Dispatch(() =>
		{
			var c = count();
			if (c <= 1)
			{
				leftButton.IsVisible = false;
				rightButton.IsVisible = false;
				return;
			}

			// Approximate content width from the known card width + spacing.
			// If the CollectionView itself hasn't been measured yet, Width
			// will be -1 and we bail out — Scrolled or the next layout pass
			// will take care of it.
			const double cardWidth = 560;
			const double spacing = 12;
			var contentWidth = (c * cardWidth) + ((c - 1) * spacing);
			var viewportWidth = collectionView.Width;

			if (viewportWidth <= 0)
			{
				// View not laid out yet — leave both hidden; the Scrolled
				// event or a subsequent pass will correct this.
				return;
			}

			var overflowsRight = contentWidth > viewportWidth;

			leftButton.IsVisible = firstVisibleIndex > 0;
			rightButton.IsVisible = overflowsRight;
		});
	}

	protected override bool OnBackButtonPressed()
	{
		if (BindingContext is EditGroupViewModel viewModel)
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
	// value here. Click handlers read from the cache to compute the next
	// scroll target.
	//
	// IMPORTANT: XAML-wired event handlers must use non-nullable `object` for
	// the sender parameter. MAUI's XAML compiler does an exact signature match
	// and rejects `object?` with error XC0002.
	private int _activeMembersFirstVisibleIndex;

	private void ActiveMembersCollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
	{
		_activeMembersFirstVisibleIndex = e.FirstVisibleItemIndex;

		var count = _viewModel.ActiveMembers.Count;
		if (count <= 1 || e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < 0)
		{
			ActiveMembersScrollLeftButton.IsVisible = false;
			ActiveMembersScrollRightButton.IsVisible = false;
			return;
		}

		ActiveMembersScrollLeftButton.IsVisible = e.FirstVisibleItemIndex > 0;
		ActiveMembersScrollRightButton.IsVisible = e.LastVisibleItemIndex < count - 1;
	}

	private void ActiveMembersScrollLeftButton_Clicked(object sender, EventArgs e)
	{
		// Scroll one position back, keeping the target card centred to match
		// the list's MandatorySingle + Center snap alignment.
		var target = Math.Max(0, _activeMembersFirstVisibleIndex - 1);
		ActiveMembersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	private void ActiveMembersScrollRightButton_Clicked(object sender, EventArgs e)
	{
		var count = _viewModel.ActiveMembers.Count;
		var target = Math.Min(count - 1, _activeMembersFirstVisibleIndex + 1);
		ActiveMembersCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	// ── PendingRemovals chevrons — same pattern as ActiveMembers ──────────
	private int _pendingRemovalsFirstVisibleIndex;

	private void PendingRemovalsCollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
	{
		_pendingRemovalsFirstVisibleIndex = e.FirstVisibleItemIndex;

		var count = _viewModel.PendingRemovals.Count;
		if (count <= 1 || e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < 0)
		{
			PendingRemovalsScrollLeftButton.IsVisible = false;
			PendingRemovalsScrollRightButton.IsVisible = false;
			return;
		}

		PendingRemovalsScrollLeftButton.IsVisible = e.FirstVisibleItemIndex > 0;
		PendingRemovalsScrollRightButton.IsVisible = e.LastVisibleItemIndex < count - 1;
	}

	private void PendingRemovalsScrollLeftButton_Clicked(object sender, EventArgs e)
	{
		var target = Math.Max(0, _pendingRemovalsFirstVisibleIndex - 1);
		PendingRemovalsCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	private void PendingRemovalsScrollRightButton_Clicked(object sender, EventArgs e)
	{
		var count = _viewModel.PendingRemovals.Count;
		var target = Math.Min(count - 1, _pendingRemovalsFirstVisibleIndex + 1);
		PendingRemovalsCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}
}