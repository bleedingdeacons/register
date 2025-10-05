using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupSelectionPage : ContentPage
{
    
    public GroupSelectionPage(GroupSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        
    }
}