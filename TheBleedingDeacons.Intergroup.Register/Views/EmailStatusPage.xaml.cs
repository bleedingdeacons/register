using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class EmailStatusPage : ContentPage
{
	private readonly EmailStatusViewModel _viewModel;

	public EmailStatusPage(EmailStatusViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);

		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	// Parameterless constructor for XAML designer support
	public EmailStatusPage()
	{
		InitializeComponent();

		// Only set a design-time viewmodel if we're in design mode
		if (Microsoft.Maui.Controls.DesignMode.IsDesignModeEnabled)
		{
			// Create a mock viewmodel for design time
			BindingContext = CreateDesignTimeViewModel();
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Refresh data when page appears. Invoke the command directly
		// on the UI thread (which is where OnAppearing already runs) —
		// don't Task.Run it. The previous RunSafeFireAndForget put the
		// command on a thread-pool thread, and the IsLoading /
		// StatusMessage property writes inside the command then raised
		// PropertyChanged on that pool thread, which the bound
		// ActivityIndicator / Label couldn't touch from a background
		// thread on Android. The command itself awaits I/O internally,
		// so the UI thread is released for the duration of the DB read.
		if (BindingContext is EmailStatusViewModel viewModel)
		{
			viewModel.LoadEmailsCommand.ExecuteAsync(null)
				.SafeFireAndForget(nameof(EmailStatusPage) + "." + nameof(OnAppearing));
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		// Dispose the transient ViewModel to stop the refresh timer
		// and unsubscribe from mail service events, preventing leaks
		// when the user navigates away.
		_viewModel?.Dispose();
	}

	private static EmailStatusViewModel CreateDesignTimeViewModel()
	{
		// Create a minimal mock for design time - you'd need to implement this
		// based on your actual dependencies, or return null and handle it
		return null!; // For now, return null - the designer will handle this
	}
}