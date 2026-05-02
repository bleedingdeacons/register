using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Reusable card-body view for displaying a position-holder Member's Name /
/// Personal Email / Mobile Phone / Rotation Date, with hold-to-reveal masking
/// on the email and phone fields driven by an internal Reveal button.
/// Mirrors <see cref="MemberCard"/> but adds the Rotation Date row used on
/// position pages.
/// </summary>
[ContentProperty(nameof(ActionContent))]
public partial class PositionMemberCard : ContentView
{
	public PositionMemberCard()
	{
		InitializeComponent();
	}

	// ── Bindable properties ───────────────────────────────────────────────

	public static readonly BindableProperty MemberProperty =
		BindableProperty.Create(
			nameof(Member),
			typeof(Member),
			typeof(PositionMemberCard),
			defaultValue: null);

	/// <summary>
	/// The Member whose details to display. Drives all field bindings inside
	/// the card, including IntergroupPositionRotation.
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
			typeof(PositionMemberCard),
			defaultValue: false,
			propertyChanged: OnIsStruckThroughChanged);

	/// <summary>
	/// When true, the value labels render with strikethrough decoration.
	/// Used by the "removed members" card variant on EditPositionPage.
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
			typeof(PositionMemberCard),
			defaultValue: TextDecorations.None);

	/// <summary>
	/// Internal-only: the actual TextDecorations applied to value labels.
	/// Computed from <see cref="IsStruckThrough"/>.
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
			typeof(PositionMemberCard),
			defaultValue: true);

	/// <summary>
	/// Whether the hold-to-reveal Reveal button is visible. Set to false for
	/// the "removed members" variant where revealing PII for someone being
	/// deleted feels wrong.
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
			typeof(PositionMemberCard),
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
		if (bindable is PositionMemberCard card)
		{
			card.ActionSlot.Content = newValue as View;
		}
	}

	private static void OnIsStruckThroughChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is PositionMemberCard card)
		{
			card.ValueTextDecorations = (bool)newValue
				? TextDecorations.Strikethrough
				: TextDecorations.None;
		}
	}

	// ── Reveal (hold-to-reveal) ───────────────────────────────────────────

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
