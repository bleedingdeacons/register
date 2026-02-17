using TheBleedingDeacons.Intergroup.Register.ViewModels;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class MeetingEditPage : ContentPage
{
    public MeetingEditPage(MeetingEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

}
