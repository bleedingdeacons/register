using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Views
{
    public partial class DatabaseBackupPage : ContentPage
    {
        private readonly DatabaseBackupViewModel _viewModel;
        public DatabaseBackupPage(DatabaseBackupViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }
    }
}