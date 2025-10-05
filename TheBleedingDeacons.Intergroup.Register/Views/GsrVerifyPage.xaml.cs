using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

//[QueryProperty(nameof(GroupId), "groupId")]
public partial class GsrRegistrationPage : ContentPage
{
    private readonly GsrVerifyViewModel _viewModel;
    
    public GsrRegistrationPage(GsrVerifyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;        
        BindingContext = _viewModel;
    }

    //protected override void OnAppearing()
    //{
    //    base.OnAppearing();
    //    _viewModel.StartListening();
    //}

    //protected override void OnDisappearing()
    //{
    //    base.OnDisappearing();
    //    _viewModel.StopListening();
    //}
}