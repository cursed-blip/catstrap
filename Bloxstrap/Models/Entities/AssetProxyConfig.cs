using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Bloxstrap.Models.Entities
{
    public class AssetProxyConfig : INotifyPropertyChanged
    {
        private bool _enabled = true;
        private string _name = "";

        public AssetProxyConfig()
        {
            Rules.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(DisplayName));
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public ObservableCollection<AssetProxyRule> Rules { get; set; } = new();

        public string DisplayName => String.IsNullOrWhiteSpace(Name) ? "Untitled config" : Name;

        public string Summary => $"{Rules.Count} rule{(Rules.Count == 1 ? "" : "s")}";

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
