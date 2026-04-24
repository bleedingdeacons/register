using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

/// <summary>
/// Hosts the combined Unity API + Better Stack settings form.
///
/// The page's job beyond wiring up the ViewModel is to guard against losing
/// unsaved changes when the user navigates away — via the Android back button,
/// the Shell flyout, or any programmatic <c>GoToAsync</c>. Both escape hatches
/// are covered:
///
/// <list type="bullet">
///   <item><see cref="OnBackButtonPressed"/> — handles the hardware / navbar back on Android.</item>
///   <item><c>Shell.Current.Navigating</c> — fires for flyout, swipe-back, and any programmatic
///         navigation originating from the Shell. This is the one that catches the "user taps
///         another flyout item" case that <c>OnBackButtonPressed</c> misses entirely.</item>
/// </list>
///
/// Both paths converge on <see cref="PromptDiscardAsync"/> so the user sees the same dialog
/// regardless of how they tried to leave.
/// </summary>
public partial class IntegrationsSettingsPage : ContentPage
{
    private readonly IntegrationsSettingsViewModel _viewModel;

    // Re-entrancy guard. When the user confirms "Discard", we cancel the
    // original navigation event and then re-issue it ourselves — that
    // re-issued navigation would otherwise fire Navigating again and
    // prompt a second time. Setting this flag tells the handler to let
    // the re-issued navigation pass through untouched.
    private bool _bypassGuard;

    public IntegrationsSettingsPage(IntegrationsSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Subscribe only while the page is visible so the handler isn't
        // still live on other pages. Unsubscribing in OnDisappearing is
        // important because Shell holds a long-lived reference.
        if (Shell.Current is Shell shell)
        {
            shell.Navigating += OnShellNavigating;
        }
    }

    protected override void OnDisappearing()
    {
        if (Shell.Current is Shell shell)
        {
            shell.Navigating -= OnShellNavigating;
        }
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        // Returning true = we handled it, suppress the default back. The
        // async prompt then either re-issues the back (via GoToAsync("..")
        // ) or leaves us on the page.
        if (_viewModel.HasUnsavedChanges && !_bypassGuard)
        {
            _ = HandleBackPressedAsync();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private async Task HandleBackPressedAsync()
    {
        if (await PromptDiscardAsync())
        {
            _bypassGuard = true;
            try
            {
                await Shell.Current.GoToAsync("..");
            }
            finally
            {
                _bypassGuard = false;
            }
        }
    }

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        // Only intercept when the user is navigating *away* from this page
        // and has unsaved changes. The guard flag short-circuits the
        // re-issued navigation after a confirmed discard.
        if (_bypassGuard) return;
        if (!_viewModel.HasUnsavedChanges) return;

        // Source == Pop / ShellItemChanged / ShellSectionChanged etc. are
        // all cases we want to guard. We don't distinguish between them —
        // any navigation away with unsaved changes gets the prompt.
        var deferral = e.GetDeferral();
        try
        {
            var shouldLeave = await PromptDiscardAsync();
            if (!shouldLeave)
            {
                e.Cancel();
            }
            else
            {
                // Allow this specific navigation to complete without
                // re-prompting. We don't need to re-issue it ourselves
                // because completing the deferral lets the original
                // navigation proceed.
                _bypassGuard = true;
            }
        }
        finally
        {
            deferral.Complete();
            // Reset the bypass on the next tick so it only covers the
            // in-flight navigation, not anything that comes after.
            Dispatcher.Dispatch(() => _bypassGuard = false);
        }
    }

    private Task<bool> PromptDiscardAsync()
    {
        return DisplayAlert(
            "Unsaved Changes",
            "You have unsaved changes on this page. Discard them and leave?",
            "Discard",
            "Keep Editing");
    }
}
