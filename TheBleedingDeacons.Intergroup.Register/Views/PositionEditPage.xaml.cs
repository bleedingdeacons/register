using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class PositionEditPage : ContentPage, IQueryAttributable
{
    private readonly PositionEditViewModel _viewModel;

    public PositionEditPage(PositionEditViewModel viewModel)
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
        EditNameEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is PositionEditViewModel viewModel)
        {
            viewModel.DoneCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}