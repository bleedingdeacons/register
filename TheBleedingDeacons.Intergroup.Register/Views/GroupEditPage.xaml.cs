using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class GroupEditPage : ContentPage
{
    public GroupEditPage(GroupEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

}