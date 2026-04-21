using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TaskExtensions = TheBleedingDeacons.Intergroup.Register.Support.TaskExtensions;

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

		// Refresh data when page appears
		if (BindingContext is EmailStatusViewModel viewModel)
		{
			TaskExtensions.RunSafeFireAndForget(
				() => viewModel.LoadEmailsCommand.ExecuteAsync(null),
				nameof(EmailStatusPage) + "." + nameof(OnAppearing));
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