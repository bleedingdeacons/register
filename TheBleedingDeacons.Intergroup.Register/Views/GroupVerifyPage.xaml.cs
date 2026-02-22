using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupVerifyPage : ContentPage, IQueryAttributable
{
    private readonly VerifyGroupViewModel _viewModel;

    public GroupVerifyPage(VerifyGroupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_viewModel is IQueryAttributable queryAttributable)
        {
            queryAttributable.ApplyQueryAttributes(query);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        System.Diagnostics.Debug.WriteLine("=== GroupVerifyPage OnAppearing ===");
        System.Diagnostics.Debug.WriteLine($"Meeting is null: {_viewModel.Meeting == null}");
        System.Diagnostics.Debug.WriteLine($"CanRegister: {_viewModel.CanRegister}");
    }
}
