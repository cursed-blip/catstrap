using System.Windows;
using System.Windows.Threading;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class AssetProxyPage
    {
        private readonly DispatcherTimer _statusTimer;

        public AssetProxyPage()
        {
            DataContext = new AssetProxyViewModel();
            InitializeComponent();

            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _statusTimer.Tick += (_, _) => ((AssetProxyViewModel)DataContext).RefreshStatus();

            Loaded += (_, _) => _statusTimer.Start();
            Unloaded += (_, _) => _statusTimer.Stop();
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not AssetProxyViewModel viewModel || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
                return;

            foreach (string file in files.Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                viewModel.ImportConfigFile(file);
        }
    }
}
