using System.Windows.Controls;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Controls
{
    public partial class DeploymentSection : UserControl
    {
        public DeploymentSection()
        {
            DataContext = new ChannelViewModel();
            InitializeComponent();
        }
    }
}
