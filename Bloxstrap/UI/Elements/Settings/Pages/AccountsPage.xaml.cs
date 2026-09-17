using System.Windows;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class AccountsPage
    {
        private readonly AccountsViewModel _viewModel;

        public AccountsPage()
        {
            _viewModel = new AccountsViewModel();

            DataContext = _viewModel;
            InitializeComponent();

            Loaded += AccountsPage_Loaded;
        }

        private void AccountsPage_Loaded(object sender, RoutedEventArgs e) => _viewModel.Load();
    }
}
