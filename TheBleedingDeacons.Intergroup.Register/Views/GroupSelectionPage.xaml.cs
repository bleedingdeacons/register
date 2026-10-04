using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupSelectionPage : ContentPage, IRegistrationWorkflow
{
    public GroupSelectionPage(GroupSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
