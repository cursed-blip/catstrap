using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using Microsoft.Win32;

using Bloxstrap.Integrations.AssetProxy;
using Bloxstrap.Models.Entities;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class AssetProxyViewModel : NotifyPropertyChangedViewModel
    {
        private static readonly ObservableCollection<AssetProxyRule> EmptyRules = new();

        private readonly RelayCommand _exportConfigCommand;
        private readonly RelayCommand _deleteConfigCommand;
        private readonly RelayCommand _deleteRuleCommand;

        private AssetProxyConfig? _selectedConfig;
        private AssetProxyRule? _selectedRule;

        public AssetProxyViewModel()
        {
            _exportConfigCommand = new RelayCommand(ExportConfig, () => HasSelectedConfig);
            _deleteConfigCommand = new RelayCommand(DeleteConfig, () => HasSelectedConfig);
            _deleteRuleCommand = new RelayCommand(DeleteRule, () => IsRuleSelected);

            App.Settings.Prop.AssetProxyConfigs ??= new ObservableCollection<AssetProxyConfig>();

            _selectedConfig = App.Settings.Prop.AssetProxyConfigs.FirstOrDefault();

            if (_selectedConfig is not null)
                _selectedRule = _selectedConfig.Rules.FirstOrDefault();

            foreach (AssetProxyConfig config in Configs)
                config.PropertyChanged += OnConfigChanged;

            Configs.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is not null)
                    foreach (AssetProxyConfig config in e.NewItems)
                        config.PropertyChanged += OnConfigChanged;

                if (e.OldItems is not null)
                    foreach (AssetProxyConfig config in e.OldItems)
                        config.PropertyChanged -= OnConfigChanged;

                InvalidateRules();
                OnPropertyChanged(nameof(ConfigCountText));
            };
        }

        public bool EnableAssetProxy
        {
            get => App.Settings.Prop.EnableAssetProxy;
            set
            {
                App.Settings.Prop.EnableAssetProxy = value;
                OnPropertyChanged(nameof(EnableAssetProxy));
            }
        }

        public bool CacheOriginalAssets
        {
            get => App.Settings.Prop.CacheOriginalAssets;
            set
            {
                App.Settings.Prop.CacheOriginalAssets = value;
                OnPropertyChanged(nameof(CacheOriginalAssets));
            }
        }

        public ObservableCollection<AssetProxyConfig> Configs => App.Settings.Prop.AssetProxyConfigs;

        public AssetProxyConfig? SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                _selectedConfig = value;
                SelectedRule = value?.Rules.FirstOrDefault();

                InvalidateRules();

                OnPropertyChanged(nameof(SelectedConfig));
                OnPropertyChanged(nameof(Rules));
                OnPropertyChanged(nameof(HasSelectedConfig));

                _exportConfigCommand.NotifyCanExecuteChanged();
                _deleteConfigCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasSelectedConfig => SelectedConfig is not null;

        public ObservableCollection<AssetProxyRule> Rules => SelectedConfig?.Rules ?? EmptyRules;

        public AssetProxyRule? SelectedRule
        {
            get => _selectedRule;
            set
            {
                _selectedRule = value;
                OnPropertyChanged(nameof(SelectedRule));
                OnPropertyChanged(nameof(IsRuleSelected));
                OnPropertyChanged(nameof(SelectedRuleFromAssetId));
                OnPropertyChanged(nameof(SelectedRuleEnabled));

                _deleteRuleCommand.NotifyCanExecuteChanged();
            }
        }

        public bool IsRuleSelected => SelectedRule is not null;

        public string SelectedRuleFromAssetId
        {
            get => SelectedRule?.FromAssetId.ToString() ?? "";
            set
            {
                if (SelectedRule is null)
                    return;

                long.TryParse(value, out long parsed);
                SelectedRule.FromAssetId = parsed;

                InvalidateRules();

                OnPropertyChanged(nameof(SelectedRuleFromAssetId));
            }
        }

        public bool SelectedRuleEnabled
        {
            get => SelectedRule?.Enabled ?? false;
            set
            {
                if (SelectedRule is null)
                    return;

                SelectedRule.Enabled = value;

                InvalidateRules();

                OnPropertyChanged(nameof(SelectedRuleEnabled));
            }
        }

        public IEnumerable<AssetProxyAction> Actions { get; } = new AssetProxyAction[]
        {
            AssetProxyAction.Replace,
            AssetProxyAction.Remove,
            AssetProxyAction.Redirect
        };

        public IReadOnlyList<string> Presets { get; } = AssetProxyConfigFiles.PresetNames;

        public IReadOnlyList<string> CacheRefreshOptions { get; } = new[]
        {
            "Keep the cache (swaps may not show)",
            "Clear when my rules change (recommended)",
            "Clear on every launch"
        };

        public string SelectedCacheRefresh
        {
            get => App.Settings.Prop.AssetProxyCacheRefresh switch
            {
                AssetProxyCacheRefresh.Never => CacheRefreshOptions[0],
                AssetProxyCacheRefresh.EveryLaunch => CacheRefreshOptions[2],
                _ => CacheRefreshOptions[1]
            };
            set
            {
                App.Settings.Prop.AssetProxyCacheRefresh = value switch
                {
                    var text when text == CacheRefreshOptions[0] => AssetProxyCacheRefresh.Never,
                    var text when text == CacheRefreshOptions[2] => AssetProxyCacheRefresh.EveryLaunch,
                    _ => AssetProxyCacheRefresh.OnRuleChange
                };

                OnPropertyChanged(nameof(SelectedCacheRefresh));
            }
        }

        private AssetProxyStatus? _hostStatus;

        public bool IsProxyRunning => _hostStatus is not null;

        public bool HasStaleHostsEntries => !IsProxyRunning && HostsFile.AreEntriesPresent();

        public Visibility RepairVisibility => HasStaleHostsEntries ? Visibility.Visible : Visibility.Collapsed;

        public string HostsStateText
        {
            get
            {
                if (HasStaleHostsEntries)
                    return "Roblox's asset hostnames are still pointed at Catstrap, but nothing is serving them - Roblox will not load assets until this is repaired";

                if (!IsProxyRunning)
                    return $"When running, these names are pointed at this PC: {string.Join(", ", AssetProxyManager.InterceptHosts)}";

                return $"Redirecting {string.Join(", ", AssetProxyManager.InterceptHosts)} to 127.0.0.1:{AssetProxyManager.OriginPort}";
            }
        }

        public string StatusText
        {
            get
            {
                if (_hostStatus is null)
                    return "Not running";

                int idle = (int)_hostStatus.ClientIdleSeconds;

                if (idle < 90)
                    return $"Intercepting on port {AssetProxyManager.OriginPort} - connected to the game";

                if (_hostStatus.RobloxRunning)
                    return $"Intercepting on port {AssetProxyManager.OriginPort} - Roblox is open but started outside Catstrap, so it is not being intercepted";

                return $"Intercepting on port {AssetProxyManager.OriginPort} - waiting for a game launched from Catstrap";
            }
        }

        public ICommand RepairHostsCommand => new RelayCommand(RepairHosts);

        public string RequestCountText => $"Requests handled: {_hostStatus?.Requests ?? 0}";

        public string ErrorCountText => _hostStatus?.Errors > 0
            ? $"Upstream failures: {_hostStatus.Errors} - assets that fail to appear usually start here"
            : "";

        public string CacheCountText => $"Assets cached: {AssetProxyManager.GetCacheFileCount()}";

        public string ConfigCountText => $"{AssetProxyManager.RuleCount} active rules across {Configs.Count(config => config.Enabled)} configs";

        public ICommand NewConfigCommand => new RelayCommand(NewConfig);

        public ICommand ImportConfigCommand => new RelayCommand(ImportConfig);

        public ICommand ExportConfigCommand => _exportConfigCommand;

        public ICommand DeleteConfigCommand => _deleteConfigCommand;

        public ICommand ApplyPresetCommand => new RelayCommand<string>(ApplyPreset);

        public ICommand OpenConfigFolderCommand => new RelayCommand(OpenConfigFolder);

        public ICommand AddRuleCommand => new RelayCommand(AddRule);

        public ICommand DeleteRuleCommand => _deleteRuleCommand;

        public ICommand StartProxyCommand => new RelayCommand(StartProxy);

        public ICommand StopProxyCommand => new RelayCommand(StopProxy);

        public ICommand OpenCacheCommand => new RelayCommand(OpenCache);

        public void RefreshStatus()
        {
            _hostStatus = AssetProxyManager.GetHostStatus(timeoutMs: 400);

            OnPropertyChanged(nameof(IsProxyRunning));
            OnPropertyChanged(nameof(HasStaleHostsEntries));
            OnPropertyChanged(nameof(RepairVisibility));
            OnPropertyChanged(nameof(HostsStateText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RequestCountText));
            OnPropertyChanged(nameof(ErrorCountText));
            OnPropertyChanged(nameof(CacheCountText));
            OnPropertyChanged(nameof(ConfigCountText));
        }

        private static void InvalidateRules() => AssetProxyManager.InvalidateRules();

        private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AssetProxyConfig.Enabled))
                return;

            InvalidateRules();
            OnPropertyChanged(nameof(ConfigCountText));
        }

        private void NewConfig()
        {
            var config = new AssetProxyConfig { Name = "New config" };

            Configs.Add(config);
            SelectedConfig = config;
        }

        private void ImportConfig()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Asset config (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = AssetProxyManager.ConfigsDirectory,
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (string path in dialog.FileNames)
                ImportConfigFile(path);
        }

        public void ImportConfigFile(string path)
        {
            const string LOG_IDENT = "AssetProxyViewModel::ImportConfigFile";

            try
            {
                AssetProxyConfigFile file = AssetProxyConfigFiles.Parse(File.ReadAllText(path));

                var config = new AssetProxyConfig
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Rules = AssetProxyConfigFiles.ToRules(file)
                };

                Configs.Add(config);
                SelectedConfig = config;

                App.Logger.WriteLine(LOG_IDENT, $"Imported {config.Rules.Count} rules from {path}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"Could not import that config.\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
            }
        }

        private void ExportConfig()
        {
            if (SelectedConfig is null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "Asset config (*.json)|*.json",
                FileName = $"{SelectedConfig.DisplayName}.json",
                InitialDirectory = AssetProxyManager.ConfigsDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                AssetProxyConfigFile file = AssetProxyConfigFiles.ToConfigFile(SelectedConfig.Rules);

                File.WriteAllText(dialog.FileName, AssetProxyConfigFiles.Serialize(file));

                App.Logger.WriteLine("AssetProxyViewModel::ExportConfig", $"Exported {dialog.FileName}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AssetProxyViewModel::ExportConfig", ex);
                Frontend.ShowMessageBox($"Could not export that config.\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
            }
        }

        private void DeleteConfig()
        {
            if (SelectedConfig is null)
                return;

            int index = Configs.IndexOf(SelectedConfig);

            Configs.Remove(SelectedConfig);

            SelectedConfig = Configs.ElementAtOrDefault(index) ?? Configs.FirstOrDefault();

            InvalidateRules();
        }

        private void ApplyPreset(string? name)
        {
            const string LOG_IDENT = "AssetProxyViewModel::ApplyPreset";

            if (String.IsNullOrEmpty(name))
                return;

            try
            {
                AssetProxyConfigFile file = AssetProxyConfigFiles.Parse(AssetProxyConfigFiles.ReadPreset(name));

                var config = new AssetProxyConfig
                {
                    Name = name,
                    Rules = AssetProxyConfigFiles.ToRules(file)
                };

                Configs.Add(config);
                SelectedConfig = config;

                App.Logger.WriteLine(LOG_IDENT, $"Added preset '{name}' with {config.Rules.Count} rules");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"Could not load the '{name}' preset.\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
            }
        }

        private void OpenConfigFolder()
        {
            Directory.CreateDirectory(AssetProxyManager.ConfigsDirectory);
            Process.Start("explorer.exe", AssetProxyManager.ConfigsDirectory);
        }

        private void AddRule()
        {
            if (SelectedConfig is null)
                NewConfig();

            var rule = new AssetProxyRule
            {
                Name = "New rule",
                Action = AssetProxyAction.Replace,
                FromAssetId = 0,
                ToAssetId = 0
            };

            Rules.Add(rule);
            SelectedRule = rule;

            InvalidateRules();
        }

        private void DeleteRule()
        {
            if (SelectedRule is null || SelectedConfig is null)
                return;

            int index = SelectedConfig.Rules.IndexOf(SelectedRule);

            SelectedConfig.Rules.Remove(SelectedRule);

            SelectedRule = SelectedConfig.Rules.ElementAtOrDefault(index) ?? SelectedConfig.Rules.FirstOrDefault();

            InvalidateRules();
        }

        private void StartProxy()
        {
            AssetProxyManager.EnsureHost();
            RefreshStatus();
        }

        private void StopProxy()
        {
            AssetProxyManager.StopHost();
            RefreshStatus();
        }

        private void RepairHosts()
        {
            AssetProxyManager.StartElevatedRepair();
        }

        private static void OpenCache()
        {
            Directory.CreateDirectory(AssetProxyManager.CacheDirectory);
            Process.Start("explorer.exe", AssetProxyManager.CacheDirectory);
        }
    }
}
