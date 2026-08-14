using CommunityToolkit.Maui.Views;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class AcceptTermsPopup : Popup
{
    private readonly TaskCompletionSource<bool> _resultTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Pixel tolerance when checking whether the ScrollView has reached its
    // bottom. ScrollY + Height rarely lands exactly on ContentSize.Height —
    // sub-pixel layout, scrollbar widths, and rounding all push it off by a
    // few units in practice. 4px is generous enough to feel reliable
    // without letting the user accept while a meaningful slice of policy
    // text is still below the fold.
    private const double ScrollEndTolerance = 4.0;

    // Scroll-hint animation. The jump-to-end arrow rises and falls to say
    // "there is more below, and you need it" — the consent row stays
    // disabled until the policy has been read to the end, and a static
    // arrow left people waiting for a checkbox that never enabled.
    private const double HintRiseDistance = 10.0;   // px travelled per bounce
    private const uint HintLegDuration = 220;       // ms for one leg
    private const int HintBouncesPerBurst = 2;      // bounces, then a rest
    private static readonly TimeSpan HintRestBetweenBursts = TimeSpan.FromSeconds(2);

    // Non-null only while the hint is running; doubles as the "already
    // started" guard, since SizeChanged fires more than once.
    private CancellationTokenSource? _hintCts;

    /// <summary>
    /// Completes with the user's choice once the popup closes:
    /// <c>true</c> if Accept was tapped, <c>false</c> if Decline was tapped
    /// or the popup closed without an explicit choice. The popup itself
    /// disables outside-tap dismissal, but we still default to <c>false</c>
    /// so an unexpected closure is treated as "did not consent".
    /// </summary>
    public Task<bool> Result => _resultTcs.Task;

    public AcceptTermsPopup(string title, string message)
    {
        InitializeComponent();
        BindingContext = new AcceptTermsPopupViewModel(this, title, message, _resultTcs);

        // Closed fires for both explicit button-driven closes and any
        // host-initiated dismissal. TrySetResult means an explicit
        // Accept/Decline always wins; otherwise we fall through to false.
        this.Closed += OnPopupClosed;

        // Drive HasScrolledToEnd from the policy scroll view. Two paths:
        //   1) The user actually scrolls to the bottom (Scrolled event).
        //   2) The body is short enough that no scrolling is required, in
        //      which case we mark scrolled-to-end as soon as the layout
        //      settles (SizeChanged on the ScrollView).
        // Without (2), a short policy would leave the I-Agree button
        // permanently disabled, since Scrolled never fires.
        PolicyScrollView.Scrolled += OnPolicyScrolled;
        PolicyScrollView.SizeChanged += OnPolicyScrollViewSizeChanged;

        // The hint exists only to say "scroll for the gate to open", so it
        // stops the instant the gate opens — by whichever route, scrolling
        // or the jump button.
        if (BindingContext is AcceptTermsPopupViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _resultTcs.TrySetResult(false);
        this.Closed -= OnPopupClosed;
        PolicyScrollView.Scrolled -= OnPolicyScrolled;
        PolicyScrollView.SizeChanged -= OnPolicyScrollViewSizeChanged;

        if (BindingContext is AcceptTermsPopupViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Cancel before the visual tree goes away: an in-flight animation
        // against a torn-down view is exactly the sort of thing that throws
        // on one platform and not the others.
        StopScrollHint();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AcceptTermsPopupViewModel.HasScrolledToEnd)) return;
        if (BindingContext is AcceptTermsPopupViewModel { HasScrolledToEnd: true })
        {
            StopScrollHint();
        }
    }

    /// <summary>
    /// Starts the arrow bouncing, if it isn't already. Idempotent: SizeChanged
    /// fires repeatedly during layout, and only the first call should take.
    /// </summary>
    private void StartScrollHint()
    {
        if (_hintCts is not null) return;

        _hintCts = new CancellationTokenSource();
        RunScrollHintAsync(_hintCts.Token).SafeFireAndForget("GDPR policy scroll hint");
    }

    private void StopScrollHint()
    {
        var cts = _hintCts;
        if (cts is null) return;

        _hintCts = null;
        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// Rises and falls a couple of times, rests, repeats — until cancelled.
    /// A continuous bounce would be nagging; a single one is missable if the
    /// user happens to be reading the top of the policy when it plays.
    /// </summary>
    private async Task RunScrollHintAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                for (var i = 0; i < HintBouncesPerBurst && !ct.IsCancellationRequested; i++)
                {
                    // *Async forms, not the bare TranslateTo: MAUI 10
                    // deprecated the originals and this repo builds clean.
                    await JumpToEndButton.TranslateToAsync(0, -HintRiseDistance, HintLegDuration, Easing.CubicOut);
                    await JumpToEndButton.TranslateToAsync(0, 0, HintLegDuration, Easing.CubicIn);
                }

                await Task.Delay(HintRestBetweenBursts, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the gate opened, or the popup closed.
        }
        finally
        {
            // Leave the arrow where it belongs even if cancelled mid-rise.
            JumpToEndButton.TranslationY = 0;
        }
    }

    private void OnPolicyScrolled(object? sender, ScrolledEventArgs e)
    {
        if (BindingContext is not AcceptTermsPopupViewModel vm) return;

        // ContentSize.Height can be 0 transiently during layout passes —
        // ignore those frames rather than incorrectly flipping the flag.
        var contentHeight = PolicyScrollView.ContentSize.Height;
        if (contentHeight <= 0) return;

        var visibleBottom = e.ScrollY + PolicyScrollView.Height;
        if (visibleBottom >= contentHeight - ScrollEndTolerance)
        {
            vm.HasScrolledToEnd = true;
        }
    }

    private void OnPolicyScrollViewSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not AcceptTermsPopupViewModel vm) return;
        if (vm.HasScrolledToEnd) return; // Already satisfied, nothing to do.

        var viewportHeight = PolicyScrollView.Height;
        var contentHeight = PolicyScrollView.ContentSize.Height;
        if (viewportHeight <= 0 || contentHeight <= 0) return;

        // Body fits without needing to scroll — treat as already read.
        if (contentHeight <= viewportHeight + ScrollEndTolerance)
        {
            vm.HasScrolledToEnd = true;
            return;
        }

        // Otherwise the policy genuinely runs past the fold, so the arrow has
        // something to say. Started here rather than on open because this is
        // the first point at which both heights are known — before layout
        // settles we cannot tell a long policy from an unmeasured one.
        StartScrollHint();
    }

    private void OnAgreementRowTapped(object? sender, TappedEventArgs e)
    {
        // Children (the CheckBox itself) consume their own taps before this
        // handler fires, so this only triggers for taps on the label or the
        // gap around it — making the whole row a comfortable hit target
        // without double-toggling when the user taps the checkbox glyph
        // directly.
        AgreementCheckBox.IsChecked = !AgreementCheckBox.IsChecked;
    }

    private async void OnJumpToEndClicked(object? sender, EventArgs e)
    {
        // "Jump to end" shortcut for users who don't want to scroll a long
        // policy line by line. We still require the scroll-to-end gate
        // because the consent rule is "you must have reached the end" —
        // jumping satisfies that rule honestly: the user has actively
        // requested to skip the prose, and the bottom of the document is
        // what they end up looking at.
        //
        // Defensive: if ContentSize hasn't been measured yet (rare, but
        // possible on a freshly-shown popup before the first layout
        // pass), bail rather than scroll to a negative offset.
        var contentHeight = PolicyScrollView.ContentSize.Height;
        var viewportHeight = PolicyScrollView.Height;
        if (contentHeight <= 0 || viewportHeight <= 0) return;

        var targetY = Math.Max(0, contentHeight - viewportHeight);
        await PolicyScrollView.ScrollToAsync(0, targetY, animated: true);

        // Belt-and-braces: the final Scrolled event after a programmatic
        // scroll doesn't always cross the tolerance threshold on every
        // platform, so set the gate flag directly. Idempotent if the
        // Scrolled handler also flips it.
        if (BindingContext is AcceptTermsPopupViewModel vm)
        {
            vm.HasScrolledToEnd = true;
        }
    }
}
