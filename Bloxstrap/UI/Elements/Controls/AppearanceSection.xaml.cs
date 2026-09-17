using System.Windows;
using System.Windows.Controls;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Controls
{
    public partial class AppearanceSection : UserControl
    {
        private AppearanceViewModel? _viewModel;

        public AppearanceSection()
        {
            InitializeComponent();
        }

        public void Initialize(Page host)
        {
            _viewModel = new AppearanceViewModel(host);
            DataContext = _viewModel;
        }

        public void Refresh() => _viewModel?.OnNavigatedTo();

        private void CustomThemeSelection(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel is null)
                return;

            _viewModel.SelectedCustomTheme = (string)((ListBox)sender).SelectedItem;
            _viewModel.SelectedCustomThemeName = _viewModel.SelectedCustomTheme;

            _viewModel.OnPropertyChanged(nameof(_viewModel.SelectedCustomTheme));
            _viewModel.OnPropertyChanged(nameof(_viewModel.SelectedCustomThemeName));
        }
    }
}
