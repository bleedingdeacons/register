using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views
{
    public partial class DiagnosticDumpPage : ContentPage
    {
        private readonly DiagnosticDumpViewModel _viewModel;
        public DiagnosticDumpPage(DiagnosticDumpViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }
    }
}