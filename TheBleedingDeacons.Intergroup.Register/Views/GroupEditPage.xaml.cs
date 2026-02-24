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
        if (_viewModel is IQueryAttributable queryAttributable)
            queryAttributable.ApplyQueryAttributes(query);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        GsrNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);
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
