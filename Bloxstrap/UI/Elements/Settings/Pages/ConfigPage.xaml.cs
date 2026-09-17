using System.Windows;

using Bloxstrap.UI.Elements.Base;
using Bloxstrap.UI.ViewModels.Settings;
using Bloxstrap.UI.Elements.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class ConfigPage
    {
        public ConfigPage()
        {
            var viewModel = new ConfigViewModel();

            viewModel.ThemeChanged += (_, _) =>
            {
                if (Window.GetWindow(this) is WpfUiWindow window)
                    window.ApplyTheme();
            };

            viewModel.BackgroundChanged += (_, _) =>
            {
                if (Window.GetWindow(this) is MainWindow window)
                    window.ApplyBackgroundGif();
            };

            DataContext = viewModel;
            InitializeComponent();
        }
    }
}
