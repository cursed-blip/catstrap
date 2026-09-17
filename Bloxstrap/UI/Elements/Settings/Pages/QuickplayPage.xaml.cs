using System.Windows;
using System.Windows.Input;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class QuickplayPage
    {
        private readonly QuickplayViewModel _viewModel;

        public QuickplayPage()
        {
            _viewModel = new QuickplayViewModel();

            DataContext = _viewModel;
            InitializeComponent();

            Loaded += QuickplayPage_Loaded;
            Unloaded += QuickplayPage_Unloaded;
        }

        private void QuickplayPage_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Load();
            _viewModel.LoadDetails();
        }

        private void QuickplayPage_Unloaded(object sender, RoutedEventArgs e) => _viewModel.Dispose();

        private void AddGameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;

            _ = _viewModel.AddFromInputAsync();
        }
    }
}
