using TheBleedingDeacons.Intergroup.Register.Controls;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class PositionVerifyPage : ContentPage, IQueryAttributable
{
	private readonly VerifyPositionViewModel _viewModel;

	public PositionVerifyPage(VerifyPositionViewModel viewModel)
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

		System.Diagnostics.Debug.WriteLine("=== PositionVerifyPage OnAppearing ===");
		System.Diagnostics.Debug.WriteLine($"Position is null: {_viewModel.Position == null}");
		System.Diagnostics.Debug.WriteLine($"CanRegister: {_viewModel.CanRegister}");

		// Prime scroll chevron visibility. See GroupEditPage for the rationale.
		_viewModel.ActiveHolders.CollectionChanged += OnActiveHoldersChanged;
		RefreshScrollChevronsDeferred();
	}

	protected override void OnDisappearing()
	{
		_viewModel.ActiveHolders.CollectionChanged -= OnActiveHoldersChanged;
		base.OnDisappearing();
	}

	// ─── Horizontal scroll chevrons ────────────────────────────────────
	// Same pattern as GroupEditPage / GroupVerifyPage.

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

	// ─── Review (hold-to-reveal) ───────────────────────────────────────
	// See GroupVerifyPage for the same pattern — the Review button lives inside
	// a DataTemplate so we walk up from the sender to find the card's reveal labels.

	private void OnReviewPressed(object sender, EventArgs e) => SetRevealState(sender, reveal: true);

	private void OnReviewReleased(object sender, EventArgs e) => SetRevealState(sender, reveal: false);

	private static void SetRevealState(object sender, bool reveal)
	{
		if (sender is not Element element) return;

		foreach (var label in FindMaskedRevealLabels(element))
		{
			if (reveal) label.Reveal(); else label.Hide();
		}
	}

	private static IEnumerable<MaskedRevealLabel> FindMaskedRevealLabels(Element startFrom)
	{
		Element? node = startFrom.Parent;
		while (node is not null)
		{
			if (node is Layout layout)
			{
				var found = Walk(layout).OfType<MaskedRevealLabel>().ToList();
				if (found.Count > 0) return found;
			}
			node = node.Parent;
		}
		return Enumerable.Empty<MaskedRevealLabel>();
	}

	private static IEnumerable<Element> Walk(Element root)
	{
		yield return root;
		if (root is IElementController controller)
		{
			foreach (var child in controller.LogicalChildren)
			{
				foreach (var d in Walk(child))
					yield return d;
			}
		}
	}
}