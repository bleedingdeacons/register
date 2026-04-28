using CommunityToolkit.Maui.Views;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class AcceptTermsPopup : Popup
{
    private readonly TaskCompletionSource<bool> _resultTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _resultTcs.TrySetResult(false);
        this.Closed -= OnPopupClosed;
    }
}
