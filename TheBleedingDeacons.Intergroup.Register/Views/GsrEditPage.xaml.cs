using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GsrEditPage : ContentPage, IQueryAttributable
{
    private EditGroupViewModel _viewModel;

    public GsrEditPage(EditGroupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // This is the key method that receives Shell navigation parameters
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        System.Diagnostics.Debug.WriteLine("=== Page ApplyQueryAttributes called ===");
        System.Diagnostics.Debug.WriteLine($"Query parameters count: {query.Count}");
        System.Diagnostics.Debug.WriteLine($"ViewModel is null: {_viewModel == null}");

        if (_viewModel == null)
        {
            System.Diagnostics.Debug.WriteLine("ERROR: ViewModel is null!");
            return;
        }

        // Forward parameters to the ViewModel
        if (_viewModel is IQueryAttributable queryAttributable)
        {
            System.Diagnostics.Debug.WriteLine("Forwarding to ViewModel...");
            queryAttributable.ApplyQueryAttributes(query);
            System.Diagnostics.Debug.WriteLine("Parameters forwarded successfully");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("ERROR: ViewModel does not implement IQueryAttributable");
        }

        // Also try setting the Meeting directly if it exists
        if (query.ContainsKey("meeting") && query["meeting"] is Meeting meeting)
        {
            System.Diagnostics.Debug.WriteLine($"Page: Setting Meeting directly - {meeting.GsrName}");
            _viewModel.Meeting = meeting;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Configure keyboard for name entry with CapitalizeWord
        GsrNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);

        System.Diagnostics.Debug.WriteLine("=== Page OnAppearing ===");
        System.Diagnostics.Debug.WriteLine($"ViewModel is null: {_viewModel == null}");

        if (_viewModel != null)
        {
            System.Diagnostics.Debug.WriteLine($"Meeting is null: {_viewModel.Meeting == null}");
            System.Diagnostics.Debug.WriteLine($"GsrName: '{_viewModel.GsrName}'");
            System.Diagnostics.Debug.WriteLine($"GsrPhone: '{_viewModel.GsrPhone}'");
            System.Diagnostics.Debug.WriteLine($"GsrEmailPersonal: '{_viewModel.GsrEmailPersonal}'");

            if (_viewModel.Meeting != null)
            {
                System.Diagnostics.Debug.WriteLine($"Meeting.GsrName: '{_viewModel.Meeting.GsrName}'");
                System.Diagnostics.Debug.WriteLine($"Meeting.GsrPhone: '{_viewModel.Meeting.GsrPhone}'");
                System.Diagnostics.Debug.WriteLine($"Meeting.GsrEmailPersonal: '{_viewModel.Meeting.GsrEmailPersonal}'");
            }
        }

        System.Diagnostics.Debug.WriteLine("======================");
    }

    // Override to handle hardware back button on Android
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is EditGroupViewModel viewModel)
        {
            // Execute the cancel command which includes unsaved changes check
            viewModel.CancelCommand.Execute(null);
            return true; // Prevent default back behavior
        }
        return base.OnBackButtonPressed();
    }
}
