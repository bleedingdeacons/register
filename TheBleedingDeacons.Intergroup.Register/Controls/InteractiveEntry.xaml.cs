using Microsoft.Maui.Controls;

namespace TheBleedingDeacons.Intergroup.Register.Controls;

/// <summary>
/// Entry with a trailing icon button whose role depends on focus, value, and
/// whether a Clear has just happened.
///
/// Combines the behaviours of <c>ClearableEntry</c> (empty the text, with an
/// Undo step) and <c>CloseableEntry</c> (dismiss the soft keyboard) in a
/// single control. The trailing slot can show one of three buttons:
///
///   • Clear (✕) — visible while the entry is unfocused and has a value.
///     Tapping it snapshots the text, empties the field, and surfaces Undo.
///   • Undo (↶) — replaces Clear after a Clear tap. Tapping it restores the
///     snapshotted text. Re-focusing the entry cancels the pending undo.
///   • Close (⌄) — visible while the entry is focused and has a value.
///     Tapping it dismisses the soft keyboard without touching the text.
///
/// Visibility is derived from state; it is not directly settable.
/// </summary>
public partial class InteractiveEntry : ContentView
{
	// ──────────────────────────────────────────────────────────────────
	// Bindable properties
	// ──────────────────────────────────────────────────────────────────

	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(InteractiveEntry), string.Empty, BindingMode.TwoWay,
			propertyChanged: (bindable, oldValue, newValue) =>
			{
				if (bindable is InteractiveEntry control && control.InternalEntry != null)
				{
					var text = newValue as string ?? string.Empty;
					if (control.InternalEntry.Text != text)
						control.InternalEntry.Text = text;
					control.UpdateButtonVisibility();
				}
			});

	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(InteractiveEntry), "Type here...");

	public static readonly BindableProperty BorderColorProperty =
		BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(InteractiveEntry), Colors.Gray);

	public static readonly BindableProperty BorderThicknessProperty =
		BindableProperty.Create(nameof(BorderThickness), typeof(double), typeof(InteractiveEntry), 1.0);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(InteractiveEntry), 5.0,
			propertyChanged: (bindable, _, _) =>
			{
				if (bindable is InteractiveEntry control)
					control.SyncTrailingButtonShapes();
			});

	public static readonly BindableProperty ControlHeightProperty =
		BindableProperty.Create(nameof(ControlHeight), typeof(double), typeof(InteractiveEntry), 48.0);

	/// <summary>
	/// Background of the Clear button. Defaults to red, matching the original
	/// ClearableEntry: a solid colour with white text reads as a deliberate,
	/// committed action.
	/// </summary>
	public static readonly BindableProperty ClearButtonColorProperty =
		BindableProperty.Create(nameof(ClearButtonColor), typeof(Color), typeof(InteractiveEntry), Colors.Red);

	/// <summary>Foreground of the Clear button label.</summary>
	public static readonly BindableProperty ClearButtonTextColorProperty =
		BindableProperty.Create(nameof(ClearButtonTextColor), typeof(Color), typeof(InteractiveEntry), Colors.White);

	/// <summary>
	/// Background of the Close button. Defaults to green — Close means "I'm done",
	/// not "destroy", matching the original CloseableEntry's intent.
	/// </summary>
	public static readonly BindableProperty CloseButtonColorProperty =
		BindableProperty.Create(nameof(CloseButtonColor), typeof(Color), typeof(InteractiveEntry), Color.FromArgb("#4CAF50"));

	/// <summary>Foreground of the Close button label.</summary>
	public static readonly BindableProperty CloseButtonTextColorProperty =
		BindableProperty.Create(nameof(CloseButtonTextColor), typeof(Color), typeof(InteractiveEntry), Colors.White);

	/// <summary>
	/// Background of the Undo button. Defaults to blue so the affordance reads
	/// as visually distinct from Clear — the slot has changed meaning, the user
	/// should notice.
	/// </summary>
	public static readonly BindableProperty UndoButtonColorProperty =
		BindableProperty.Create(nameof(UndoButtonColor), typeof(Color), typeof(InteractiveEntry), Color.FromArgb("#2196F3"));

	/// <summary>Foreground of the Undo button label.</summary>
	public static readonly BindableProperty UndoButtonTextColorProperty =
		BindableProperty.Create(nameof(UndoButtonTextColor), typeof(Color), typeof(InteractiveEntry), Colors.White);

	public static readonly BindableProperty FontSizeProperty =
		BindableProperty.Create(nameof(FontSize), typeof(double), typeof(InteractiveEntry), 14.0);

	public static readonly BindableProperty TextColorProperty =
		BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(InteractiveEntry), Colors.Black);

	public static readonly BindableProperty PlaceholderColorProperty =
		BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(InteractiveEntry), Colors.Gray);

	public static readonly BindableProperty KeyboardProperty =
		BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(InteractiveEntry), Keyboard.Default);

	public static readonly BindableProperty IsPasswordProperty =
		BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(InteractiveEntry), false);

	public static readonly BindableProperty MaxLengthProperty =
		BindableProperty.Create(nameof(MaxLength), typeof(int), typeof(InteractiveEntry), int.MaxValue);

	public static readonly BindableProperty IsReadOnlyProperty =
		BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(InteractiveEntry), false,
			propertyChanged: (bindable, _, _) =>
			{
				if (bindable is InteractiveEntry control)
					control.UpdateButtonVisibility();
			});

	public static readonly BindableProperty HorizontalTextAlignmentProperty =
		BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(InteractiveEntry), TextAlignment.Start);

	public static readonly BindableProperty VerticalTextAlignmentProperty =
		BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(InteractiveEntry), TextAlignment.Center);

	// ──────────────────────────────────────────────────────────────────
	// CLR property accessors
	// ──────────────────────────────────────────────────────────────────

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

	public Color ClearButtonTextColor
	{
		get => (Color)GetValue(ClearButtonTextColorProperty);
		set => SetValue(ClearButtonTextColorProperty, value);
	}

	public Color CloseButtonColor
	{
		get => (Color)GetValue(CloseButtonColorProperty);
		set => SetValue(CloseButtonColorProperty, value);
	}

	public Color CloseButtonTextColor
	{
		get => (Color)GetValue(CloseButtonTextColorProperty);
		set => SetValue(CloseButtonTextColorProperty, value);
	}

	public Color UndoButtonColor
	{
		get => (Color)GetValue(UndoButtonColorProperty);
		set => SetValue(UndoButtonColorProperty, value);
	}

	public Color UndoButtonTextColor
	{
		get => (Color)GetValue(UndoButtonTextColorProperty);
		set => SetValue(UndoButtonTextColorProperty, value);
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

	// ──────────────────────────────────────────────────────────────────
	// Events
	// ──────────────────────────────────────────────────────────────────

	public event EventHandler<TextChangedEventArgs>? TextChanged;
	public event EventHandler? TextCleared;
	public event EventHandler? TextRestored;
	public event EventHandler? Closed;

	// ──────────────────────────────────────────────────────────────────
	// State machine
	// ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Trailing button currently shown, derived from focus + value + undo state.
	/// </summary>
	private enum TrailingButtonMode { None, Clear, Close, Undo }

	/// <summary>True while the inner Entry currently holds focus.</summary>
	private bool _entryHasFocus;

	/// <summary>
	/// True between Clear being tapped and the entry next gaining focus.
	/// While true, the trailing slot shows Undo instead of Clear, allowing
	/// the user to restore <see cref="_previousText"/>.
	/// </summary>
	private bool _undoPending;

	/// <summary>
	/// Snapshot of the text that was wiped by the most recent Clear tap.
	/// Restored verbatim by Undo.
	/// </summary>
	private string _previousText = string.Empty;

	public InteractiveEntry()
	{
		InitializeComponent();

		// Pick up whatever CornerRadius was set on the control (default or via XAML)
		// before the first paint — the propertyChanged callback only fires on changes
		// from the default, not on initial assignment.
		SyncTrailingButtonShapes();

		// Suppress the platform-default underline / bottom border on the inner Entry.
		// We hook HandlerChanged because the platform view doesn't exist until the
		// handler is created — calling immediately after InitializeComponent would
		// be a no-op on most platforms. The handler is also recreated on theme /
		// parent changes, so keeping the subscription live is what we want.
		InternalEntry.HandlerChanged += (_, _) => RemoveEntryUnderline(InternalEntry);
	}

	/// <summary>
	/// Strip the platform-default underline / bottom border from the given Entry's
	/// native view. MAUI's cross-platform <see cref="Entry"/> exposes no property
	/// for this: on Android the line is part of the EditText background drawable,
	/// on Windows it's the bottom edge of the TextBox border. iOS / MacCatalyst
	/// have no underline by default, so this is a no-op there.
	///
	/// Inlined into the control rather than split across <c>Platforms/</c> partials
	/// because it's only ever called from one place.
	/// </summary>
	private static void RemoveEntryUnderline(Microsoft.Maui.Controls.Entry entry)
	{
		// Reference the parameter unconditionally so iOS / MacCatalyst builds
		// (where the try-block is empty) don't flag it as unused.
		_ = entry;

		try
		{
#if ANDROID
			// Replace the EditText background — a state-list drawable that draws the
			// underline — with a transparent ColorDrawable. Using a transparent drawable
			// rather than null avoids some Android themes redrawing a stark default border.
			if (entry?.Handler?.PlatformView is Android.Widget.EditText editText)
			{
				editText.Background = new Android.Graphics.Drawables.ColorDrawable(
					Android.Graphics.Color.Transparent);
			}
#elif WINDOWS
			// Zero the TextBox border and override the visual-state brushes the default
			// style uses on focus / pointer-over, otherwise interaction redraws the line.
			if (entry?.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
			{
				var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(
					Microsoft.UI.Colors.Transparent);

				textBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
				textBox.BorderBrush = transparent;
				textBox.Resources["TextControlBorderBrush"] = transparent;
				textBox.Resources["TextControlBorderBrushPointerOver"] = transparent;
				textBox.Resources["TextControlBorderBrushFocused"] = transparent;
				textBox.Resources["TextControlBorderBrushDisabled"] = transparent;
			}
#endif
			// iOS / MacCatalyst: UITextField has no platform-default underline. Nothing to do.
		}
		catch
		{
			// Pure cosmetic — never throw out of this.
		}
	}

	// ──────────────────────────────────────────────────────────────────
	// Event handlers
	// ──────────────────────────────────────────────────────────────────

	private void OnTextChanged(object sender, TextChangedEventArgs e)
	{
		UpdateButtonVisibility();
		TextChanged?.Invoke(this, e);
	}

	private void OnEntryFocused(object sender, FocusEventArgs e)
	{
		_entryHasFocus = true;
		// Focusing the entry cancels any pending undo: the user is interacting
		// fresh, so the Clear/Close logic takes over again.
		_undoPending = false;
		UpdateButtonVisibility();
	}

	private void OnEntryUnfocused(object sender, FocusEventArgs e)
	{
		_entryHasFocus = false;
		UpdateButtonVisibility();
	}

	private void OnClearClicked(object sender, EventArgs e)
	{
		// Clear is offered only while unfocused, so we don't need (or want) to
		// touch focus here. Snapshot the text so Undo can restore it, then empty
		// the field and latch the Undo affordance.
		//
		// Order matters: set the snapshot and latch BEFORE assigning Text. The
		// Text setter triggers OnTextChanged → UpdateButtonVisibility synchronously,
		// and we want the resolver to already see the post-clear state.
		_previousText = InternalEntry?.Text ?? string.Empty;
		_undoPending = true;
		Text = string.Empty;
		UpdateButtonVisibility();
		TextCleared?.Invoke(this, EventArgs.Empty);
	}

	private void OnUndoClicked(object sender, EventArgs e)
	{
		// Restore the snapshot and drop the undo latch. The trailing slot
		// reverts to Clear on the next visibility update because the field
		// once again has a value and is unfocused.
		_undoPending = false;
		Text = _previousText;
		_previousText = string.Empty;
		UpdateButtonVisibility();
		TextRestored?.Invoke(this, EventArgs.Empty);
	}

	private void OnCloseClicked(object sender, EventArgs e)
	{
		// Mirrors CloseableEntry: explicitly dismiss the IME on Android (no-op
		// elsewhere) and drop entry focus so subsequent state is "no buttons"
		// (or Clear, if the field has a value).
		KeyboardHelper.HideKeyboard(InternalEntry);
		InternalEntry.Unfocus();
		UpdateButtonVisibility();
		Closed?.Invoke(this, EventArgs.Empty);
	}

	// ──────────────────────────────────────────────────────────────────
	// Visibility derivation
	// ──────────────────────────────────────────────────────────────────

	private void UpdateButtonVisibility()
	{
		// We toggle the wrapper Borders, not the inner Buttons: the wrapper
		// carries the asymmetric corner shape and the visible background colour,
		// so hiding it is what removes the affordance from view.
		if (ClearButtonWrapper == null || CloseButtonWrapper == null || UndoButtonWrapper == null)
			return;

		var mode = ResolveMode();

		ClearButtonWrapper.IsVisible = mode == TrailingButtonMode.Clear;
		CloseButtonWrapper.IsVisible = mode == TrailingButtonMode.Close;
		UndoButtonWrapper.IsVisible  = mode == TrailingButtonMode.Undo;
	}

	/// <summary>
	/// Keep the trailing-button wrapper shapes in lockstep with the outer
	/// SimpleEntry's <see cref="CornerRadius"/>. Each wrapper rounds its
	/// top-right and bottom-right corners only, so the button visually nests
	/// inside the outer rounded edge while presenting a flat left side
	/// against the entry text.
	///
	/// <see cref="RoundRectangle.CornerRadius"/> is a four-value struct, not
	/// a single double, so we can't bind it directly from the control's
	/// <c>double</c>-typed CornerRadius — hence this helper.
	/// </summary>
	private void SyncTrailingButtonShapes()
	{
		// `this.CornerRadius` (a double, the SimpleEntry property) vs the
		// `CornerRadius` type below — qualified to avoid any ambiguity for
		// the reader, even though the compiler would resolve correctly.
		var radius = new CornerRadius(0, this.CornerRadius, 0, this.CornerRadius);

		if (ClearButtonShape != null) ClearButtonShape.CornerRadius = radius;
		if (CloseButtonShape != null) CloseButtonShape.CornerRadius = radius;
		if (UndoButtonShape  != null) UndoButtonShape.CornerRadius  = radius;
	}

	private TrailingButtonMode ResolveMode()
	{
		// Read-only entries never get a trailing button — there's nothing to
		// clear, restore, or dismiss.
		if (IsReadOnly)
			return TrailingButtonMode.None;

		var hasValue = !string.IsNullOrEmpty(InternalEntry?.Text);

		// Focused: only Close is relevant, and only when there's a value.
		// (An empty focused field shows no trailing button.)
		if (_entryHasFocus)
			return hasValue ? TrailingButtonMode.Close : TrailingButtonMode.None;

		// Unfocused: Undo wins over Clear if it's pending. Undo is checked
		// before the value test because the field is empty in that state.
		if (_undoPending)
			return TrailingButtonMode.Undo;

		// Unfocused with content → Clear. Empty unfocused field → nothing.
		return hasValue ? TrailingButtonMode.Clear : TrailingButtonMode.None;
	}

	// ──────────────────────────────────────────────────────────────────
	// Public API
	// ──────────────────────────────────────────────────────────────────

	/// <summary>Move keyboard focus to the inner Entry.</summary>
	public new bool Focus() => InternalEntry.Focus();
}
