using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

public partial class ClearableEntry : ContentView
{
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(ClearableEntry), string.Empty, BindingMode.TwoWay,
			propertyChanged: (bindable, oldValue, newValue) =>
			{
				if (bindable is ClearableEntry control && control.InternalEntry != null)
				{
					var text = newValue as string ?? string.Empty;
					if (control.InternalEntry.Text != text)
						control.InternalEntry.Text = text;
				}
			});

	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(ClearableEntry), "Type here...");

	public static readonly BindableProperty BorderColorProperty =
		BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(ClearableEntry), Colors.Gray);

	public static readonly BindableProperty BorderThicknessProperty =
		BindableProperty.Create(nameof(BorderThickness), typeof(double), typeof(ClearableEntry), 1.0);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(ClearableEntry), 5.0);

	public static readonly BindableProperty ControlHeightProperty =
		BindableProperty.Create(nameof(ControlHeight), typeof(double), typeof(ClearableEntry), 40.0);

	public static readonly BindableProperty ClearButtonColorProperty =
		BindableProperty.Create(nameof(ClearButtonColor), typeof(Color), typeof(ClearableEntry), Colors.Gray);

	public static readonly BindableProperty FontSizeProperty =
		BindableProperty.Create(nameof(FontSize), typeof(double), typeof(ClearableEntry), 14.0);

	public static readonly BindableProperty TextColorProperty =
		BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(ClearableEntry), Colors.Black);

	public static readonly BindableProperty PlaceholderColorProperty =
		BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(ClearableEntry), Colors.Gray);

	public static readonly BindableProperty KeyboardProperty =
		BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(ClearableEntry), Keyboard.Default);

	public static readonly BindableProperty IsPasswordProperty =
		BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(ClearableEntry), false);

	public static readonly BindableProperty MaxLengthProperty =
		BindableProperty.Create(nameof(MaxLength), typeof(int), typeof(ClearableEntry), int.MaxValue);

	public static readonly BindableProperty IsReadOnlyProperty =
		BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(ClearableEntry), false);

	public static readonly BindableProperty HorizontalTextAlignmentProperty =
		BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(ClearableEntry), TextAlignment.Start);

	public static readonly BindableProperty VerticalTextAlignmentProperty =
		BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(ClearableEntry), TextAlignment.Center);

	public ClearableEntry()
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

	public Color ClearButtonColor
	{
		get => (Color)GetValue(ClearButtonColorProperty);
		set => SetValue(ClearButtonColorProperty, value);
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
	public event EventHandler? TextCleared;

	private void OnTextChanged(object sender, TextChangedEventArgs e)
	{
		// Show/hide clear button based on text content
		ClearButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

		// Raise the TextChanged event
		TextChanged?.Invoke(this, e);
	}

	private void OnClearClicked(object sender, EventArgs e)
	{
		// Clear the text entry
		Text = string.Empty;

		// Give focus back to the textbox
		InternalEntry.Focus();

		// Raise the TextCleared event
		TextCleared?.Invoke(this, EventArgs.Empty);
	}

	// Public method to focus the entry
	public new bool Focus()
	{
		return InternalEntry.Focus();
	}
}