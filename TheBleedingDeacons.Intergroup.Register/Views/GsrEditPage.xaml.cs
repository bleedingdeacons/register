using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GsrEditPage : ContentPage, IQueryAttributable
{
    private GsrEditViewModel _viewModel;

    public GsrEditPage(GsrEditViewModel viewModel)
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

        // Also try setting the Group directly if it exists
        if (query.ContainsKey("group") && query["group"] is Group group)
        {
            System.Diagnostics.Debug.WriteLine($"Page: Setting Group directly - {group.GsrName}");
            _viewModel.Group = group;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        System.Diagnostics.Debug.WriteLine("=== Page OnAppearing ===");
        System.Diagnostics.Debug.WriteLine($"ViewModel is null: {_viewModel == null}");

        if (_viewModel != null)
        {
            System.Diagnostics.Debug.WriteLine($"Group is null: {_viewModel.Group == null}");
            System.Diagnostics.Debug.WriteLine($"GsrName: '{_viewModel.GsrName}'");
            System.Diagnostics.Debug.WriteLine($"GsrPhone: '{_viewModel.GsrPhone}'");
            System.Diagnostics.Debug.WriteLine($"GsrEmailPersonal: '{_viewModel.GsrEmailPersonal}'");

            if (_viewModel.Group != null)
            {
                System.Diagnostics.Debug.WriteLine($"Group.GsrName: '{_viewModel.Group.GsrName}'");
                System.Diagnostics.Debug.WriteLine($"Group.GsrPhone: '{_viewModel.Group.GsrPhone}'");
                System.Diagnostics.Debug.WriteLine($"Group.GsrEmailPersonal: '{_viewModel.Group.GsrEmailPersonal}'");
            }
        }

        System.Diagnostics.Debug.WriteLine("======================");
    }

    // Override to handle hardware back button on Android
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is GsrEditViewModel viewModel)
        {
            // Execute the cancel command which includes unsaved changes check
            viewModel.CancelCommand.Execute(null);
            return true; // Prevent default back behavior
        }
        return base.OnBackButtonPressed();
    }
}