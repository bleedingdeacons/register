using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class PositionVerifyPage : ContentPage, IQueryAttributable
{
    private readonly VerifyPositionViewModel _viewModel;

    public PositionVerifyPage(VerifyPositionViewModel viewModel)
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

        System.Diagnostics.Debug.WriteLine("=== PositionVerifyPage OnAppearing ===");
        System.Diagnostics.Debug.WriteLine($"Position is null: {_viewModel.Position == null}");
        System.Diagnostics.Debug.WriteLine($"CanRegister: {_viewModel.CanRegister}");
    }
}