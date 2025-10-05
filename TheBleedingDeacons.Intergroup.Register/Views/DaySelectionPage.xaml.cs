using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class DaySelectionPage : ContentPage
{    

    public DaySelectionPage(DaySelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
 
    }
}