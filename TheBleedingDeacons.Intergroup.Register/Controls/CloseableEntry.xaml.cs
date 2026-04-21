using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Entry with a trailing Close button that dismisses the soft keyboard.
///
/// Visibility of the Close button is derived, not directly settable: it appears
/// only when the entry is focused AND the entry has a non-empty value AND the
/// entry is editable. Pressing Close unfocuses the entry (which closes the soft
/// keyboard on Android/iOS) but leaves the text intact.
///
/// Compare to <see cref="ClearableEntry"/>, which clears the text.
/// </summary>
public partial class CloseableEntry : ContentView
{
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(CloseableEntry), string.Empty, BindingMode.TwoWay,
			propertyChanged: (bindable, oldValue, newValue) =>
			{
				if (bindable is CloseableEntry control && control.InternalEntry != null)
				{
					var text = newValue as string ?? string.Empty;
					if (control.InternalEntry.Text != text)
						control.InternalEntry.Text = text;
					control.UpdateCloseButtonVisibility();
				}
			});

	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(CloseableEntry), "Type here...");

	public static readonly BindableProperty BorderColorProperty =
		BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(CloseableEntry), Colors.Gray);

	public static readonly BindableProperty BorderThicknessProperty =
		BindableProperty.Create(nameof(BorderThickness), typeof(double), typeof(CloseableEntry), 1.0);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(CloseableEntry), 5.0);

	public static readonly BindableProperty ControlHeightProperty =
		BindableProperty.Create(nameof(ControlHeight), typeof(double), typeof(CloseableEntry), 40.0);

	/// <summary>
	/// Background colour of the Close button. Defaults to green — the whole point of
	/// the control is that the close affordance reads as "finish / commit", not "destroy".
	/// </summary>
	public static readonly BindableProperty CloseButtonColorProperty =
		BindableProperty.Create(nameof(CloseButtonColor), typeof(Color), typeof(CloseableEntry), Color.FromArgb("#4CAF50"));

	public static readonly BindableProperty FontSizeProperty =
		BindableProperty.Create(nameof(FontSize), typeof(double), typeof(CloseableEntry), 14.0);

	public static readonly BindableProperty TextColorProperty =
		BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(CloseableEntry), Colors.Black);

	public static readonly BindableProperty PlaceholderColorProperty =
		BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(CloseableEntry), Colors.Gray);

	public static readonly BindableProperty KeyboardProperty =
		BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(CloseableEntry), Keyboard.Default);

	public static readonly BindableProperty IsPasswordProperty =
		BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(CloseableEntry), false);

	public static readonly BindableProperty MaxLengthProperty =
		BindableProperty.Create(nameof(MaxLength), typeof(int), typeof(CloseableEntry), int.MaxValue);

	public static readonly BindableProperty IsReadOnlyProperty =
		BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(CloseableEntry), false,
			propertyChanged: (bindable, _, _) =>
			{
				if (bindable is CloseableEntry control)
					control.UpdateCloseButtonVisibility();
			});

	public static readonly BindableProperty HorizontalTextAlignmentProperty =
		BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(CloseableEntry), TextAlignment.Start);

	public static readonly BindableProperty VerticalTextAlignmentProperty =
		BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(CloseableEntry), TextAlignment.Center);

	public CloseableEntry()
	{
		InitializeComponent();
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string Placeholder
	{
		get => (string)GetValue(PlaceholderProperty);
		set => SetValue(PlaceholderProperty, value);
	}

	public Color BorderColor
	{
		get => (Color)GetValue(BorderColorProperty);
		set => SetValue(BorderColorProperty, value);
	}

	public double BorderThickness
	{
		get => (double)GetValue(BorderThicknessProperty);
		set => SetValue(BorderThicknessProperty, value);
	}

	public double CornerRadius
	{
		get => (double)GetValue(CornerRadiusProperty);
		set => SetValue(CornerRadiusProperty, value);
	}

	public double ControlHeight
	{
		get => (double)GetValue(ControlHeightProperty);
		set => SetValue(ControlHeightProperty, value);
	}

	public Color CloseButtonColor
	{
		get => (Color)GetValue(CloseButtonColorProperty);
		set => SetValue(CloseButtonColorProperty, value);
	}

	public double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	public Color TextColor
	{
		get => (Color)GetValue(TextColorProperty);
		set => SetValue(TextColorProperty, value);
	}

	public Color PlaceholderColor
	{
		get => (Color)GetValue(PlaceholderColorProperty);
		set => SetValue(PlaceholderColorProperty, value);
	}

	public Keyboard Keyboard
	{
		get => (Keyboard)GetValue(KeyboardProperty);
		set => SetValue(KeyboardProperty, value);
	}

	public bool IsPassword
	{
		get => (bool)GetValue(IsPasswordProperty);
		set => SetValue(IsPasswordProperty, value);
	}

	public int MaxLength
	{
		get => (int)GetValue(MaxLengthProperty);
		set => SetValue(MaxLengthProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public TextAlignment HorizontalTextAlignment
	{
		get => (TextAlignment)GetValue(HorizontalTextAlignmentProperty);
		set => SetValue(HorizontalTextAlignmentProperty, value);
	}

	public TextAlignment VerticalTextAlignment
	{
		get => (TextAlignment)GetValue(VerticalTextAlignmentProperty);
		set => SetValue(VerticalTextAlignmentProperty, value);
	}

	// Events
	public event EventHandler<TextChangedEventArgs>? TextChanged;
	public event EventHandler? Closed;

	/// <summary>
	/// Tracks whether the inner Entry currently has focus. Kept in sync via Focused / Unfocused
	/// events (there's no public "is focused" bindable to read reliably across platforms).
	/// </summary>
	private bool _entryHasFocus;

	private void OnTextChanged(object sender, TextChangedEventArgs e)
	{
		UpdateCloseButtonVisibility();
		TextChanged?.Invoke(this, e);
	}

	private void OnEntryFocused(object sender, FocusEventArgs e)
	{
		_entryHasFocus = true;
		UpdateCloseButtonVisibility();
	}

	private void OnEntryUnfocused(object sender, FocusEventArgs e)
	{
		_entryHasFocus = false;
		UpdateCloseButtonVisibility();
	}

	private void OnCloseClicked(object sender, EventArgs e)
	{
		// Android's IME stays up after Unfocus(), so we also ask the platform to dismiss.
		// On other platforms HideKeyboard is a no-op; Unfocus() alone is sufficient there.
		KeyboardHelper.HideKeyboard(InternalEntry);
		InternalEntry.Unfocus();
		Closed?.Invoke(this, EventArgs.Empty);
	}

	private void UpdateCloseButtonVisibility()
	{
		// Button is visible only while all three conditions hold.
		// Note: on platforms where IsFocused can be read directly we'd bind to it,
		// but tracking it via events is more reliable across MAUI targets.
		CloseButton.IsVisible = _entryHasFocus
			&& !IsReadOnly
			&& !string.IsNullOrEmpty(InternalEntry?.Text);
	}

	// Public method to focus the entry
	public new bool Focus() => InternalEntry.Focus();
}