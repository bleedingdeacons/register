using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Reusable card-body view for displaying a Member's Name / Personal Email /
/// Mobile Phone, with hold-to-reveal masking on the email and phone fields
/// driven by an internal Reveal button. The bottom action row (e.g. Edit /
/// Remove, Undo) is supplied by the host page via <see cref="ActionContent"/>,
/// which is the control's ContentProperty so it can be written as the
/// MemberCard's direct child.
/// </summary>
[ContentProperty(nameof(ActionContent))]
public partial class MemberCard : ContentView
{
	public MemberCard()
	{
		InitializeComponent();
	}

	// ── Bindable properties ───────────────────────────────────────────────

	public static readonly BindableProperty MemberProperty =
		BindableProperty.Create(
			nameof(Member),
			typeof(Member),
			typeof(MemberCard),
			defaultValue: null);

	/// <summary>
	/// The Member whose details to display. Drives all field bindings
	/// inside the card.
	/// </summary>
	public Member? Member
	{
		get => (Member?)GetValue(MemberProperty);
		set => SetValue(MemberProperty, value);
	}

	public static readonly BindableProperty IsStruckThroughProperty =
		BindableProperty.Create(
			nameof(IsStruckThrough),
			typeof(bool),
			typeof(MemberCard),
			defaultValue: false,
			propertyChanged: OnIsStruckThroughChanged);

	/// <summary>
	/// When true, the value labels render with strikethrough decoration.
	/// Used by the "removed members" card variant on EditGroupPage.
	/// </summary>
	public bool IsStruckThrough
	{
		get => (bool)GetValue(IsStruckThroughProperty);
		set => SetValue(IsStruckThroughProperty, value);
	}

	public static readonly BindableProperty ValueTextDecorationsProperty =
		BindableProperty.Create(
			nameof(ValueTextDecorations),
			typeof(TextDecorations),
			typeof(MemberCard),
			defaultValue: TextDecorations.None);

	/// <summary>
	/// Internal-only: the actual TextDecorations applied to value labels.
	/// Computed from <see cref="IsStruckThrough"/>; exposed as a bindable
	/// property so the XAML can bind to it directly without a converter.
	/// </summary>
	public TextDecorations ValueTextDecorations
	{
		get => (TextDecorations)GetValue(ValueTextDecorationsProperty);
		private set => SetValue(ValueTextDecorationsProperty, value);
	}

	public static readonly BindableProperty ShowRevealButtonProperty =
		BindableProperty.Create(
			nameof(ShowRevealButton),
			typeof(bool),
			typeof(MemberCard),
			defaultValue: true);

	/// <summary>
	/// Whether the hold-to-reveal Reveal button is visible. Set to false
	/// for the "removed members" variant where revealing PII for someone
	/// being deleted feels wrong.
	/// </summary>
	public bool ShowRevealButton
	{
		get => (bool)GetValue(ShowRevealButtonProperty);
		set => SetValue(ShowRevealButtonProperty, value);
	}

	public static readonly BindableProperty ActionContentProperty =
		BindableProperty.Create(
			nameof(ActionContent),
			typeof(View),
			typeof(MemberCard),
			defaultValue: null,
			propertyChanged: OnActionContentChanged);

	/// <summary>
	/// Caller-supplied action row rendered below the field block. Anything
	/// bound here inherits Member as its local BindingContext, so
	/// CommandParameter="{Binding .}" resolves to the Member, matching the
	/// pre-extraction DataTemplate behaviour. RelativeSource bindings to
	/// page-level ViewModels continue to walk the visual tree as normal.
	/// </summary>
	public View? ActionContent
	{
		get => (View?)GetValue(ActionContentProperty);
		set => SetValue(ActionContentProperty, value);
	}

	private static void OnActionContentChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is MemberCard card)
		{
			card.ActionSlot.Content = newValue as View;
		}
	}

	private static void OnIsStruckThroughChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is MemberCard card)
		{
			card.ValueTextDecorations = (bool)newValue
				? TextDecorations.Strikethrough
				: TextDecorations.None;
		}
	}

	// ── Reveal (hold-to-reveal) ───────────────────────────────────────────
	// Because EmailReveal and MobileReveal are named children of THIS
	// control (not a DataTemplate), we can call them directly — no need
	// for the tree-walking helper that the page-level handlers used.

	private void OnRevealPressed(object sender, EventArgs e)
	{
		EmailReveal.Reveal();
		MobileReveal.Reveal();
	}

	private void OnRevealReleased(object sender, EventArgs e)
	{
		EmailReveal.Hide();
		MobileReveal.Hide();
	}
}
