using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Material 3 page shell used by every user-flow page.
/// Owns: page background, status bar tint, context header strip, content padding,
/// bottom action bar, and the loading overlay.
///
/// Pages must not set their own root padding, background, or StatusBarBehavior —
/// put all that in this one place so everything moves together.
/// </summary>
public partial class PageScaffold : ContentView
{
    public PageScaffold()
    {
        InitializeComponent();
    }

    // ─── Content slots ────────────────────────────────────────────────

    public static readonly BindableProperty ContentBodyProperty =
        BindableProperty.Create(nameof(ContentBody), typeof(View), typeof(PageScaffold));

    /// <summary>The main page body (usually a ScrollView or a CollectionView).</summary>
    public View ContentBody
    {
        get => (View)GetValue(ContentBodyProperty);
        set => SetValue(ContentBodyProperty, value);
    }

    public static readonly BindableProperty ActionBarBodyProperty =
        BindableProperty.Create(nameof(ActionBarBody), typeof(View), typeof(PageScaffold));

    /// <summary>Content of the fixed bottom action bar. Typically a Grid of 1–2 buttons.</summary>
    public View ActionBarBody
    {
        get => (View)GetValue(ActionBarBodyProperty);
        set => SetValue(ActionBarBodyProperty, value);
    }

    // ─── Action bar control ───────────────────────────────────────────

    public static readonly BindableProperty HasActionBarProperty =
        BindableProperty.Create(nameof(HasActionBar), typeof(bool), typeof(PageScaffold), false);

    public bool HasActionBar
    {
        get => (bool)GetValue(HasActionBarProperty);
        set => SetValue(HasActionBarProperty, value);
    }

    // ─── Context header ───────────────────────────────────────────────

    public static readonly BindableProperty ContextHeaderTextProperty =
        BindableProperty.Create(nameof(ContextHeaderText), typeof(string), typeof(PageScaffold), string.Empty);

    public string ContextHeaderText
    {
        get => (string)GetValue(ContextHeaderTextProperty);
        set => SetValue(ContextHeaderTextProperty, value);
    }

    public static readonly BindableProperty ContextHeaderIsVisibleProperty =
        BindableProperty.Create(nameof(ContextHeaderIsVisible), typeof(bool), typeof(PageScaffold), false);

    public bool ContextHeaderIsVisible
    {
        get => (bool)GetValue(ContextHeaderIsVisibleProperty);
        set => SetValue(ContextHeaderIsVisibleProperty, value);
    }

    // ─── Loading overlay ──────────────────────────────────────────────

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(PageScaffold), false);

    public new bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly BindableProperty BusyMessageProperty =
        BindableProperty.Create(nameof(BusyMessage), typeof(string), typeof(PageScaffold), string.Empty);

    public string BusyMessage
    {
        get => (string)GetValue(BusyMessageProperty);
        set => SetValue(BusyMessageProperty, value);
    }

    // ─── Padding override ─────────────────────────────────────────────

    public static readonly BindableProperty ContentPaddingProperty =
        BindableProperty.Create(nameof(ContentPadding), typeof(Thickness), typeof(PageScaffold), new Thickness(16));

    /// <summary>Padding applied around the content body. Default = 16 (design gutter).</summary>
    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }
}
