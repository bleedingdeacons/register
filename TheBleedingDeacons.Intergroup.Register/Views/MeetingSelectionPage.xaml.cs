using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class MeetingSelectionPage : ContentPage
{
    
    public MeetingSelectionPage(MeetingSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        
    }
}
