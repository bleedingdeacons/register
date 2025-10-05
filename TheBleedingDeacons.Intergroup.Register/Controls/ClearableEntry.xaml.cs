using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

public partial class ClearableEntry : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ClearableEntry), string.Empty, BindingMode.TwoWay);

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

    // Events
    public event EventHandler<TextChangedEventArgs> TextChanged;
    public event EventHandler TextCleared;

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