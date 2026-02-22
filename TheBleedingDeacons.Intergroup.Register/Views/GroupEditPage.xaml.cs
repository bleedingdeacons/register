using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupEditPage : ContentPage, IQueryAttributable
{
    private readonly EditGroupViewModel _viewModel;

    public GroupEditPage(EditGroupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        System.Diagnostics.Debug.WriteLine("=== GroupEditPage ApplyQueryAttributes called ===");

        if (_viewModel == null)
        {
            System.Diagnostics.Debug.WriteLine("ERROR: ViewModel is null!");
            return;
        }

        if (_viewModel is IQueryAttributable queryAttributable)
        {
            queryAttributable.ApplyQueryAttributes(query);
        }

        // Also try setting the Meeting directly if it exists
        if (query.ContainsKey("meeting") && query["meeting"] is Meeting meeting)
        {
            System.Diagnostics.Debug.WriteLine($"Page: Setting Meeting directly - {meeting.Group?.Gsrs.FirstOrDefault()?.Name}");
            _viewModel.Meeting = meeting;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Configure keyboard for name entry with CapitalizeWord
        GsrNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);

        System.Diagnostics.Debug.WriteLine("=== GroupEditPage OnAppearing ===");
        System.Diagnostics.Debug.WriteLine($"Meeting is null: {_viewModel?.Meeting == null}");
        System.Diagnostics.Debug.WriteLine($"GsrName: '{_viewModel?.GsrName}'");
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is EditGroupViewModel viewModel)
        {
            viewModel.CancelCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}