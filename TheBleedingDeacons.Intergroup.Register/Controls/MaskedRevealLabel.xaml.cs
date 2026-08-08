using System.Linq;
using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

public enum MaskType
{
	Email,
	Mobile
}

public partial class MaskedRevealLabel : ContentView
{
	public static readonly BindableProperty ValueProperty =
		BindableProperty.Create(nameof(Value), typeof(string), typeof(MaskedRevealLabel), string.Empty, BindingMode.OneWay,
			propertyChanged: OnValueOrMaskChanged);

	public static readonly BindableProperty MaskTypeProperty =
		BindableProperty.Create(nameof(MaskType), typeof(MaskType), typeof(MaskedRevealLabel), MaskType.Email,
			propertyChanged: OnValueOrMaskChanged);

	public static readonly BindableProperty DisplayTextProperty =
		BindableProperty.Create(nameof(DisplayText), typeof(string), typeof(MaskedRevealLabel), string.Empty);

	public static readonly BindableProperty HasValueProperty =
		BindableProperty.Create(nameof(HasValue), typeof(bool), typeof(MaskedRevealLabel), false);

	public static readonly BindableProperty BorderColorProperty =
		BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(MaskedRevealLabel), Colors.Gray);

	public static readonly BindableProperty BorderThicknessProperty =
		BindableProperty.Create(nameof(BorderThickness), typeof(double), typeof(MaskedRevealLabel), 1.0);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(MaskedRevealLabel), 5.0);

	public static readonly BindableProperty ControlHeightProperty =
		BindableProperty.Create(nameof(ControlHeight), typeof(double), typeof(MaskedRevealLabel), 40.0);

	public static readonly BindableProperty RevealButtonColorProperty =
		BindableProperty.Create(nameof(RevealButtonColor), typeof(Color), typeof(MaskedRevealLabel), Colors.Gray);

	public static readonly BindableProperty FontSizeProperty =
		BindableProperty.Create(nameof(FontSize), typeof(double), typeof(MaskedRevealLabel), 14.0);

	public static readonly BindableProperty TextColorProperty =
		BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(MaskedRevealLabel), Colors.Black);

	public static readonly BindableProperty HorizontalTextAlignmentProperty =
		BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(MaskedRevealLabel), TextAlignment.Start);

	public static readonly BindableProperty VerticalTextAlignmentProperty =
		BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(MaskedRevealLabel), TextAlignment.Center);

	/// <summary>
	/// Character used to mask the value (default: ●)
	/// </summary>
	public static readonly BindableProperty MaskCharacterProperty =
		BindableProperty.Create(nameof(MaskCharacter), typeof(char), typeof(MaskedRevealLabel), '●',
			propertyChanged: OnValueOrMaskChanged);

	/// <summary>
	/// Number of trailing digits left visible when masking a mobile number (default: 4)
	/// </summary>
	public static readonly BindableProperty VisibleMobileDigitsProperty =
		BindableProperty.Create(nameof(VisibleMobileDigits), typeof(int), typeof(MaskedRevealLabel), 4,
			propertyChanged: OnValueOrMaskChanged);

	/// <summary>
	/// Whether the built-in press-and-hold Show button is displayed (default: true).
	/// Set to false when the parent view provides its own reveal trigger and drives the
	/// control via the public Reveal() / Hide() methods.
	/// </summary>
	public static readonly BindableProperty ShowRevealButtonProperty =
		BindableProperty.Create(nameof(ShowRevealButton), typeof(bool), typeof(MaskedRevealLabel), true,
			propertyChanged: OnShowRevealButtonChanged);

	/// <summary>
	/// Derived: RevealButton is only shown when the caller allows it AND there is a value to reveal.
	/// Bound directly by the XAML; never set manually.
	/// </summary>
	public static readonly BindableProperty IsRevealButtonVisibleProperty =
		BindableProperty.Create(nameof(IsRevealButtonVisible), typeof(bool), typeof(MaskedRevealLabel), false);

	public MaskedRevealLabel()
	{
		InitializeComponent();
		UpdateRevealButtonVisibility();
		UpdateDisplay(revealed: false);
	}

	public string Value
	{
		get => (string)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	public MaskType MaskType
	{
		get => (MaskType)GetValue(MaskTypeProperty);
		set => SetValue(MaskTypeProperty, value);
	}

	public string DisplayText
	{
		get => (string)GetValue(DisplayTextProperty);
		private set => SetValue(DisplayTextProperty, value);
	}

	public bool HasValue
	{
		get => (bool)GetValue(HasValueProperty);
		private set => SetValue(HasValueProperty, value);
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

	public Color RevealButtonColor
	{
		get => (Color)GetValue(RevealButtonColorProperty);
		set => SetValue(RevealButtonColorProperty, value);
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

	public char MaskCharacter
	{
		get => (char)GetValue(MaskCharacterProperty);
		set => SetValue(MaskCharacterProperty, value);
	}

	public int VisibleMobileDigits
	{
		get => (int)GetValue(VisibleMobileDigitsProperty);
		set => SetValue(VisibleMobileDigitsProperty, value);
	}

	public bool ShowRevealButton
	{
		get => (bool)GetValue(ShowRevealButtonProperty);
		set => SetValue(ShowRevealButtonProperty, value);
	}

	public bool IsRevealButtonVisible
	{
		get => (bool)GetValue(IsRevealButtonVisibleProperty);
		private set => SetValue(IsRevealButtonVisibleProperty, value);
	}

	// Events
	public event EventHandler? Revealed;
	public event EventHandler? Hidden;

	private static void OnValueOrMaskChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is MaskedRevealLabel control)
		{
			control.HasValue = !string.IsNullOrEmpty(control.Value);
			control.UpdateRevealButtonVisibility();
			control.UpdateDisplay(revealed: false);
		}
	}

	private static void OnShowRevealButtonChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is MaskedRevealLabel control)
		{
			control.UpdateRevealButtonVisibility();
		}
	}

	private void UpdateRevealButtonVisibility()
	{
		// Only show the built-in button when the caller allows it AND there's a value to reveal.
		IsRevealButtonVisible = ShowRevealButton && HasValue;
	}

	/// <summary>
	/// Reveal the value. Intended for parent views that manage their own reveal trigger
	/// (see ShowRevealButton). Fires Revealed. Idempotent.
	/// </summary>
	public void Reveal()
	{
		UpdateDisplay(revealed: true);
		Revealed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Re-mask the value. Pair with Reveal(). Fires Hidden. Idempotent.
	/// </summary>
	public void Hide()
	{
		UpdateDisplay(revealed: false);
		Hidden?.Invoke(this, EventArgs.Empty);
	}

	private void OnRevealPressed(object sender, EventArgs e)
	{
		UpdateDisplay(revealed: true);
		Revealed?.Invoke(this, EventArgs.Empty);
	}

	private void OnRevealReleased(object sender, EventArgs e)
	{
		UpdateDisplay(revealed: false);
		Hidden?.Invoke(this, EventArgs.Empty);
	}

	private void UpdateDisplay(bool revealed)
	{
		var value = Value ?? string.Empty;

		if (string.IsNullOrEmpty(value))
		{
			DisplayText = string.Empty;
			return;
		}

		DisplayText = revealed ? value : Mask(value);
	}

	private string Mask(string value)
	{
		return MaskType switch
		{
			MaskType.Mobile => MaskMobile(value),
			MaskType.Email => MaskEmail(value),
			_ => new string(MaskCharacter, value.Length)
		};
	}

	private string MaskMobile(string value)
	{
		// Extract only digits from the string (handles formatted phone numbers)
		string digitsOnly = new string(value.Where(char.IsDigit).ToArray());

		if (digitsOnly.Length == 0)
			return new string(MaskCharacter, value.Length);

		// If string has fewer or equal digits than visible count, obscure all
		if (digitsOnly.Length <= VisibleMobileDigits)
			return new string(MaskCharacter, digitsOnly.Length);

		// Obscure all except last N digits
		string lastDigits = digitsOnly.Substring(digitsOnly.Length - VisibleMobileDigits);
		string obscured = new string(MaskCharacter, digitsOnly.Length - VisibleMobileDigits);
		return $"{obscured} {lastDigits}";
	}

	private string MaskEmail(string value)
	{
		int atIndex = value.IndexOf('@');

		// No '@' — treat the whole thing as opaque and mask it all
		if (atIndex <= 0)
			return new string(MaskCharacter, value.Length);

		string local = value.Substring(0, atIndex);
		string domain = value.Substring(atIndex); // includes '@'

		// Show the first character of the local part, mask the rest, keep the domain intact
		string maskedLocal = local.Length == 1
			? new string(MaskCharacter, 1)
			: local[0] + new string(MaskCharacter, local.Length - 1);

		return maskedLocal + domain;
	}
}