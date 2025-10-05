using CommunityToolkit.Maui.Views;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class CountdownPopup : Popup
{
    public CountdownPopup(string title, string message, Func<Task> navigateAction)
    {
        InitializeComponent();
        BindingContext = new CountdownPopupViewModel(this, title, message, navigateAction);

        // Subscribe to the Closed event for cleanup
        this.Closed += OnPopupClosed;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (BindingContext is CountdownPopupViewModel viewModel)
        {
            viewModel.Dispose();
        }

        // Unsubscribe to prevent memory leaks
        this.Closed -= OnPopupClosed;
    }
}