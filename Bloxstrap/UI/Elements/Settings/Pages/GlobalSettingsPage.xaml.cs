using System.Windows;
using System.Windows.Input;

using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Common.Interfaces;
using Wpf.Ui.Mvvm.Contracts;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class GlobalSettingsPage : INavigationAware
    {
        private GlobalSettingsViewModel _viewModel = null!;

        public GlobalSettingsPage()
        {
            SetupViewModel();
            InitializeComponent();

            AppearanceHost.Initialize(this);
        }

        private void SetupViewModel()
        {
            _viewModel = new GlobalSettingsViewModel();

            DataContext = _viewModel;
        }

        public void OnNavigatedTo()
        {
            AppearanceHost.Refresh();

            _viewModel.RefreshRenderingPresets();
        }

        public void OnNavigatedFrom() { }

        private void ValidateUInt32(object sender, TextCompositionEventArgs e) => e.Handled = !UInt32.TryParse(e.Text, out uint _);
        private void ValidateFloat(object sender, TextCompositionEventArgs e) => e.Handled = !Regex.IsMatch(e.Text, @"^\d*\.?\d*$");
    }
}
