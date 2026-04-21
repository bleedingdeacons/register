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