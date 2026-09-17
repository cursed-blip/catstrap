using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using Bloxstrap.Integrations;
using Bloxstrap.Integrations.AssetProxy;
using Bloxstrap.Models.Entities;
using Bloxstrap.UI.Theming;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ConfigViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "ConfigViewModel";

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly RelayCommand _loadSelectedCommand;
        private readonly RelayCommand _deleteSelectedCommand;

        private string? _selectedConfig;

        private readonly DispatcherTimer _accentCommitTimer;

        public ConfigViewModel()
        {
            _loadSelectedCommand = new RelayCommand(LoadSelected, () => HasSelectedConfig);
            _deleteSelectedCommand = new RelayCommand(DeleteSelected, () => HasSelectedConfig);

            _accentCommitTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            _accentCommitTimer.Tick += (_, _) =>
            {
                _accentCommitTimer.Stop();
                CommitAccent();
            };

            Refresh();
        }

        public event EventHandler? ThemeChanged;

        public static string ConfigsDirectory => Path.Combine(Paths.Base, "Configs");

        public ObservableCollection<string> SavedConfigs { get; } = new();

        public string? SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                _selectedConfig = value;

                OnPropertyChanged(nameof(SelectedConfig));
                OnPropertyChanged(nameof(HasSelectedConfig));

                _loadSelectedCommand.NotifyCanExecuteChanged();
                _deleteSelectedCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasSelectedConfig => !String.IsNullOrWhiteSpace(SelectedConfig);

        public string ConfigsDirectoryText => ConfigsDirectory;

        private bool _accentDirty;

        public Color AccentPrimaryColor
        {
            get => AccentTheme.Primary;
            set
            {
                string hex = AccentTheme.ToHex(value);

                if (App.Settings.Prop.AccentColor == hex)
                    return;

                App.Settings.Prop.AccentColor = hex;
                _accentDirty = true;

                RefreshAccentProperties();
                QueueAccentCommit();
            }
        }

        public Brush AccentPreviewBrush => AccentTheme.MakeBrush();

        private void QueueAccentCommit()
        {
            _accentCommitTimer.Stop();
            _accentCommitTimer.Start();
        }

        public void CommitAccent()
        {
            if (!_accentDirty)
                return;

            _accentDirty = false;

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshAccentProperties()
        {
            OnPropertyChanged(nameof(AccentPrimaryColor));
            OnPropertyChanged(nameof(AccentPreviewBrush));
        }

        public bool AutoSaveAccounts
        {
            get => App.Settings.Prop.AutoSaveAccounts;
            set
            {
                if (App.Settings.Prop.AutoSaveAccounts == value)
                    return;

                App.Settings.Prop.AutoSaveAccounts = value;
                OnPropertyChanged(nameof(AutoSaveAccounts));
            }
        }

        public event EventHandler? BackgroundChanged;

        private string _backgroundStatus = "";

        public string BackgroundStatus
        {
            get => _backgroundStatus;
            private set
            {
                _backgroundStatus = value;
                OnPropertyChanged(nameof(BackgroundStatus));
            }
        }

        public string BackgroundGifUrl
        {
            get => App.Settings.Prop.BackgroundGifUrl;
            set
            {
                if (App.Settings.Prop.BackgroundGifUrl == value)
                    return;

                App.Settings.Prop.BackgroundGifUrl = value;
                OnPropertyChanged(nameof(BackgroundGifUrl));
            }
        }

        public double BackgroundGifOpacity
        {
            get => App.Settings.Prop.BackgroundGifOpacity;
            set
            {
                double clamped = Math.Clamp(value, 0.05, 1);

                if (Math.Abs(App.Settings.Prop.BackgroundGifOpacity - clamped) < 0.001)
                    return;

                App.Settings.Prop.BackgroundGifOpacity = clamped;

                OnPropertyChanged(nameof(BackgroundGifOpacity));
                BackgroundChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ICommand ApplyBackgroundCommand => new AsyncRelayCommand(ApplyBackgroundAsync);

        public ICommand ClearBackgroundCommand => new RelayCommand(ClearBackground);

        private async Task ApplyBackgroundAsync()
        {
            string url = BackgroundGifUrl?.Trim() ?? "";

            if (url.Length == 0)
            {
                BackgroundStatus = "Paste a link to a .gif first.";
                return;
            }

            BackgroundStatus = "Downloading...";

            try
            {
                await BackgroundGif.EnsureAsync(url);

                BackgroundStatus = "Playing.";

                BackgroundChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                BackgroundStatus = ex.Message;
            }
        }

        private void ClearBackground()
        {
            BackgroundGifUrl = "";
            BackgroundStatus = "Background cleared.";

            BackgroundChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool DeveloperMode
        {
            get => App.Settings.Prop.DeveloperMode;
            set
            {
                App.Settings.Prop.DeveloperMode = value;
                OnPropertyChanged(nameof(DeveloperMode));
            }
        }

        public bool EnableAnalytics
        {
            get => App.Settings.Prop.EnableAnalytics;
            set
            {
                App.Settings.Prop.EnableAnalytics = value;
                OnPropertyChanged(nameof(EnableAnalytics));
            }
        }

        public bool WPFSoftwareRender
        {
            get => App.Settings.Prop.WPFSoftwareRender;
            set
            {
                App.Settings.Prop.WPFSoftwareRender = value;
                OnPropertyChanged(nameof(WPFSoftwareRender));
            }
        }

        public bool ReduceVisualEffects
        {
            get => App.Settings.Prop.ReduceVisualEffects;
            set
            {
                App.Settings.Prop.ReduceVisualEffects = value;
                OnPropertyChanged(nameof(ReduceVisualEffects));
            }
        }

        public IReadOnlyDictionary<string, int> IconPrefetchOptions { get; } = new Dictionary<string, int>
        {
            { "None (load as I scroll)", 0 },
            { "6 - slowest machines", 6 },
            { "12 - balanced", 12 },
            { "24 - everything at once", 24 }
        };

        public string SelectedIconPrefetch
        {
            get => IconPrefetchOptions.FirstOrDefault(x => x.Value == App.Settings.Prop.QuickplayIconPrefetch).Key
                ?? IconPrefetchOptions.First(x => x.Value == 12).Key;
            set
            {
                if (!IconPrefetchOptions.TryGetValue(value, out int amount))
                    return;

                App.Settings.Prop.QuickplayIconPrefetch = amount;
                OnPropertyChanged(nameof(SelectedIconPrefetch));
            }
        }

        public IReadOnlyDictionary<string, int> IconCacheOptions { get; } = new Dictionary<string, int>
        {
            { "32 MB", 32 },
            { "64 MB - default", 64 },
            { "128 MB", 128 },
            { "256 MB", 256 },
            { "No limit", 0 }
        };

        public string SelectedIconCacheLimit
        {
            get => IconCacheOptions.FirstOrDefault(x => x.Value == App.Settings.Prop.ThumbnailCacheLimitMb).Key
                ?? IconCacheOptions.First(x => x.Value == 64).Key;
            set
            {
                if (!IconCacheOptions.TryGetValue(value, out int limit))
                    return;

                App.Settings.Prop.ThumbnailCacheLimitMb = limit;

                if (limit > 0)
                    Quickplay.PruneThumbnailCache();

                OnPropertyChanged(nameof(SelectedIconCacheLimit));
            }
        }

        public bool BlockRobloxTelemetry
        {
            get => App.Settings.Prop.BlockRobloxTelemetry;
            set
            {
                App.Settings.Prop.BlockRobloxTelemetry = value;

                App.FastFlags.SetTelemetryPreset(value);

                OnPropertyChanged(nameof(BlockRobloxTelemetry));
            }
        }

        public bool OfflineMode
        {
            get => App.Settings.Prop.ForceLocalData;
            set
            {
                App.Settings.Prop.ForceLocalData = value;
                OnPropertyChanged(nameof(OfflineMode));
            }
        }

        public bool CheckForUpdates
        {
            get => App.Settings.Prop.CheckForUpdates;
            set
            {
                App.Settings.Prop.CheckForUpdates = value;
                OnPropertyChanged(nameof(CheckForUpdates));
            }
        }

        public ICommand RefreshCommand => new RelayCommand(Refresh);

        public ICommand SaveCurrentCommand => new RelayCommand(SaveCurrent);

        public ICommand LoadSelectedCommand => _loadSelectedCommand;

        public ICommand ImportCommand => new RelayCommand(() => ImportFrom(null));

        public ICommand DeleteSelectedCommand => _deleteSelectedCommand;

        public ICommand OpenFolderCommand => new RelayCommand(OpenFolder);

        public ICommand OpenAboutCommand => new RelayCommand(OpenAbout);

        public ICommand OpenLogsCommand => new RelayCommand(OpenLogs);

        public ICommand ResetSettingsCommand => new RelayCommand(ResetSettings);

        public void Refresh()
        {
            string? previous = SelectedConfig;

            SavedConfigs.Clear();

            try
            {
                Directory.CreateDirectory(ConfigsDirectory);

                foreach (string file in Directory.GetFiles(ConfigsDirectory, "*.cfg").OrderBy(Path.GetFileName))
                    SavedConfigs.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            SelectedConfig = previous is not null && SavedConfigs.Contains(previous) ? previous : SavedConfigs.FirstOrDefault();
        }

        private void SaveCurrent()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Catstrap config (*.cfg)|*.cfg",
                FileName = "My Catstrap setup.cfg",
                InitialDirectory = ConfigsDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                Write(dialog.FileName);

                App.Logger.WriteLine(LOG_IDENT, $"Exported config to {dialog.FileName}");

                Frontend.ShowMessageBox($"Saved your setup to\n{dialog.FileName}\n\nSend that file to anyone and they can load it in.", System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"Could not save the config.\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
            }

            Refresh();
        }

        private void LoadSelected()
        {
            if (String.IsNullOrWhiteSpace(SelectedConfig))
                return;

            ImportFrom(Path.Combine(ConfigsDirectory, SelectedConfig + ".cfg"));
        }

        private void ImportFrom(string? path)
        {
            if (path is null)
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Catstrap config (*.cfg;*.json)|*.cfg;*.json|All files (*.*)|*.*",
                    InitialDirectory = ConfigsDirectory
                };

                if (dialog.ShowDialog() != true)
                    return;

                path = dialog.FileName;
            }

            if (!File.Exists(path))
                return;

            try
            {
                CatstrapConfigPackage? package = JsonSerializer.Deserialize<CatstrapConfigPackage>(File.ReadAllText(path));

                if (package is null)
                    throw new InvalidDataException("Config file was empty");

                Apply(package);

                App.Logger.WriteLine(LOG_IDENT, $"Loaded config from {path}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"Could not load that config.\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
            }
        }

        private void Write(string path)
        {
            string? directory = Path.GetDirectoryName(path);

            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var package = new CatstrapConfigPackage
            {
                Settings = App.Settings.Prop,
                FastFlags = App.FastFlags.Prop
            };

            File.WriteAllText(path, JsonSerializer.Serialize(package, WriteOptions));
        }

        private void Apply(CatstrapConfigPackage package)
        {
            if (package.Settings is not null)
            {
                bool cookieAccess = App.Settings.Prop.AllowCookieAccess;
                bool autoSaveAccounts = App.Settings.Prop.AutoSaveAccounts;

                App.Settings.Prop = package.Settings;
                App.Settings.Prop.AllowCookieAccess = cookieAccess;
                App.Settings.Prop.AutoSaveAccounts = autoSaveAccounts;
                App.Settings.Prop.MigrateAssetProxyRules();
                App.Settings.Save();
            }

            if (package.FastFlags is not null)
            {
                App.FastFlags.Prop = new Dictionary<string, object>(package.FastFlags);
                App.FastFlags.Save();
            }

            AssetProxyManager.InvalidateRules();

            _accentDirty = true;
            RefreshAccentProperties();
            CommitAccent();
            OnPropertyChanged(nameof(DeveloperMode));
            OnPropertyChanged(nameof(EnableAnalytics));
            OnPropertyChanged(nameof(WPFSoftwareRender));
            OnPropertyChanged(nameof(ReduceVisualEffects));
            OnPropertyChanged(nameof(SelectedIconPrefetch));
            OnPropertyChanged(nameof(SelectedIconCacheLimit));
            OnPropertyChanged(nameof(BlockRobloxTelemetry));
            OnPropertyChanged(nameof(OfflineMode));
            OnPropertyChanged(nameof(CheckForUpdates));
            OnPropertyChanged(nameof(AutoSaveAccounts));

            ThemeChanged?.Invoke(this, EventArgs.Empty);

            Frontend.ShowMessageBox("Config loaded. Restart Catstrap (and Roblox) for every change to take effect.", System.Windows.MessageBoxImage.Information);
        }

        private void DeleteSelected()
        {
            if (String.IsNullOrWhiteSpace(SelectedConfig))
                return;

            string path = Path.Combine(ConfigsDirectory, SelectedConfig + ".cfg");

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            Refresh();
        }

        private static void OpenFolder()
        {
            Directory.CreateDirectory(ConfigsDirectory);
            Process.Start("explorer.exe", ConfigsDirectory);
        }

        private static void OpenLogs()
        {
            Directory.CreateDirectory(Paths.Logs);
            Process.Start("explorer.exe", Paths.Logs);
        }

        private void ResetSettings()
        {
            var result = Frontend.ShowMessageBox(
                "Reset every Catstrap setting back to default?\n\nThis also clears your asset proxy configs and Quickplay list. Any .cfg files you exported stay in the configs folder.",
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            App.Settings.Prop = new Models.Persistable.Settings();
            App.Settings.Save();

            App.FastFlags.SetTelemetryPreset(App.Settings.Prop.BlockRobloxTelemetry);

            AssetProxyManager.InvalidateRules();

            _accentDirty = true;
            RefreshAccentProperties();
            CommitAccent();
            OnPropertyChanged(nameof(DeveloperMode));
            OnPropertyChanged(nameof(EnableAnalytics));
            OnPropertyChanged(nameof(WPFSoftwareRender));
            OnPropertyChanged(nameof(ReduceVisualEffects));
            OnPropertyChanged(nameof(SelectedIconPrefetch));
            OnPropertyChanged(nameof(SelectedIconCacheLimit));
            OnPropertyChanged(nameof(BlockRobloxTelemetry));
            OnPropertyChanged(nameof(OfflineMode));
            OnPropertyChanged(nameof(CheckForUpdates));
            OnPropertyChanged(nameof(AutoSaveAccounts));

            ThemeChanged?.Invoke(this, EventArgs.Empty);

            App.Logger.WriteLine(LOG_IDENT, "Settings were reset to defaults");

            Frontend.ShowMessageBox("Catstrap settings restored to default.", System.Windows.MessageBoxImage.Information);
        }

        private static void OpenAbout() => new UI.Elements.About.MainWindow().ShowDialog();
    }
}
