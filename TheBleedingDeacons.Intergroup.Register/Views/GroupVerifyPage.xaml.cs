using TheBleedingDeacons.Intergroup.Register.Controls;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupVerifyPage : ContentPage, IQueryAttributable
{
	private readonly VerifyGroupViewModel _viewModel;

	public GroupVerifyPage(VerifyGroupViewModel viewModel)
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

		System.Diagnostics.Debug.WriteLine("=== GroupVerifyPage OnAppearing ===");
		System.Diagnostics.Debug.WriteLine($"Group is null: {_viewModel.Group == null}");
		System.Diagnostics.Debug.WriteLine($"CanRegister: {_viewModel.CanRegister}");

		// Prime scroll chevron visibility — the Scrolled event won't fire on
		// first layout, so we refresh manually here and whenever the collection
		// changes. Mirrors GroupEditPage.
		_viewModel.ActiveGsrs.CollectionChanged += OnActiveGsrsChanged;
		RefreshScrollChevronsDeferred();
	}

	protected override void OnDisappearing()
	{
		_viewModel.ActiveGsrs.CollectionChanged -= OnActiveGsrsChanged;
		base.OnDisappearing();
	}

	// ─── Horizontal scroll chevrons ────────────────────────────────────
	// Same pattern as GroupEditPage. XAML-wired handlers must use
	// non-nullable `object` sender or the XAML compiler rejects them with XC0002.

	private int _activeGsrsFirstVisibleIndex;

	private void OnActiveGsrsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		RefreshScrollChevronsDeferred();
	}

	private void RefreshScrollChevronsDeferred()
	{
		Dispatcher.Dispatch(() =>
		{
			var count = _viewModel.ActiveGsrs.Count;
			if (count <= 1)
			{
				ActiveGsrsScrollLeftButton.IsVisible = false;
				ActiveGsrsScrollRightButton.IsVisible = false;
				return;
			}

			const double cardWidth = 560;
			const double spacing = 12;
			var contentWidth = (count * cardWidth) + ((count - 1) * spacing);
			var viewportWidth = ActiveGsrsCollectionView.Width;

			if (viewportWidth <= 0) return;

			ActiveGsrsScrollLeftButton.IsVisible = _activeGsrsFirstVisibleIndex > 0;
			ActiveGsrsScrollRightButton.IsVisible = contentWidth > viewportWidth;
		});
	}

	private void ActiveGsrsCollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
	{
		_activeGsrsFirstVisibleIndex = e.FirstVisibleItemIndex;

		var count = _viewModel.ActiveGsrs.Count;
		if (count <= 1 || e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < 0)
		{
			ActiveGsrsScrollLeftButton.IsVisible = false;
			ActiveGsrsScrollRightButton.IsVisible = false;
			return;
		}

		ActiveGsrsScrollLeftButton.IsVisible = e.FirstVisibleItemIndex > 0;
		ActiveGsrsScrollRightButton.IsVisible = e.LastVisibleItemIndex < count - 1;
	}

	private void ActiveGsrsScrollLeftButton_Clicked(object sender, EventArgs e)
	{
		var target = Math.Max(0, _activeGsrsFirstVisibleIndex - 1);
		ActiveGsrsCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	private void ActiveGsrsScrollRightButton_Clicked(object sender, EventArgs e)
	{
		var count = _viewModel.ActiveGsrs.Count;
		var target = Math.Min(count - 1, _activeGsrsFirstVisibleIndex + 1);
		ActiveGsrsCollectionView.ScrollTo(target, position: ScrollToPosition.Center, animate: true);
	}

	// ─── Review (hold-to-reveal) ───────────────────────────────────────
	// Review lives inside a DataTemplate, so x:Name doesn't resolve from the page.
	// We walk up from the Button through its parents to find the sibling
	// MaskedRevealLabel controls inside the same card and toggle them.

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
		// Walk up from the pressed button. The first ancestor layout is the card's
		// content stack; scan its tree for any MaskedRevealLabel.
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