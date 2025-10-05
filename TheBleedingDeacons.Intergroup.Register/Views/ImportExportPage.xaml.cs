using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views;

public partial class ImportExportPage : ContentPage
{
    public ImportExportPage(ImportExportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}