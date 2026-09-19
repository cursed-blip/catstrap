using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        private readonly AsyncRelayCommand _cleanNowCommand;

        public BehaviourViewModel()
        {
            _cleanNowCommand = new AsyncRelayCommand(CleanNowAsync);

            App.Cookies.StateChanged += (object? _, CookieState state) => CookieLoadingFailed = state != CookieState.Success && state != CookieState.Unknown;

            Task.Run(LoadRegionsAsync);
        }

        public System.Windows.Input.ICommand CleanNowCommand => _cleanNowCommand;

        public bool IsRobloxInstallationMissing => String.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid) && String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);

        public bool CookieAccess
        {
            get => App.Settings.Prop.AllowCookieAccess;
            set
            {
                App.Settings.Prop.AllowCookieAccess = value;
                if (value)
                    Task.Run(App.Cookies.LoadCookies);

                OnPropertyChanged(nameof(CookieAccess));
            }
        }

        // guh
        private bool _cookieLoadingFailed;
        public bool CookieLoadingFailed
        {
            get => _cookieLoadingFailed;
            set
            {
                _cookieLoadingFailed = value;
                OnPropertyChanged(nameof(CookieLoadingFailed));
            }
        }

        public bool EnableBetterMatchmaking
        {
            get => App.Settings.Prop.EnableBetterMatchmaking;
            set => App.Settings.Prop.EnableBetterMatchmaking = value;
        }

        public bool EnableBetterMatchmakingRandomization
        {
            get => App.Settings.Prop.EnableBetterMatchmakingRandomization;
            set => App.Settings.Prop.EnableBetterMatchmakingRandomization = value;
        }

        public string SelectedRegion
        {
            get => App.Settings.Prop.SelectedRegion;
            set => App.Settings.Prop.SelectedRegion = value;
        }

        private List<string> _availableRegions = new() { Integrations.MatchmakingRegions.AutoRegion };

        public List<string> AvailableRegions
        {
            get => _availableRegions;
            set
            {
                _availableRegions = value;
                OnPropertyChanged(nameof(AvailableRegions));
            }
        }

        private async Task CleanNowAsync()
        {
            System.Windows.MessageBoxResult result = Frontend.ShowMessageBox(
                "Delete the Roblox logs and caches that are older than the age picked above? Anything currently in use is skipped.",
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxButton.YesNo
            );

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            await Task.Run(Integrations.Cleaner.DoCleaning);
        }

        private async Task LoadRegionsAsync()
        {
            string current = SelectedRegion;

            try
            {
                List<string> regions = await Integrations.MatchmakingRegions.GetRegionNamesAsync();

                var list = new List<string>(regions.Count + 2) { Integrations.MatchmakingRegions.AutoRegion };
                list.AddRange(regions);

                if (!String.IsNullOrWhiteSpace(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase))
                    list.Add(current);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    AvailableRegions = list;
                    OnPropertyChanged(nameof(SelectedRegion));
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("BehaviourViewModel::LoadRegionsAsync", $"Failed to load regions: {ex.Message}");
            }
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        public bool ForceRobloxLanguage
        {
            get => App.Settings.Prop.ForceRobloxLanguage;
            set => App.Settings.Prop.ForceRobloxLanguage = value;
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set => App.Settings.Prop.BackgroundUpdatesEnabled = value;
        }

        public CleanerOptions SelectedCleanUpMode
        {
            get => App.Settings.Prop.CleanerOptions;
            set => App.Settings.Prop.CleanerOptions = value;
        }

        public IEnumerable<CleanerOptions> CleanerOptions { get; } = CleanerOptionsEx.Selections;

        public CleanerOptions CleanerOption
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
            }
        }

        private List<string> CleanerItems = App.Settings.Prop.CleanerDirectories;

        public bool CleanerLogs
        {
            get => CleanerItems.Contains("RobloxLogs");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxLogs");
                else
                    CleanerItems.Remove("RobloxLogs"); // should we try catch it?
            }
        }

        public bool CleanerCache
        {
            get => CleanerItems.Contains("RobloxCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxCache");
                else
                    CleanerItems.Remove("RobloxCache");
            }
        }

        public bool CleanerStudioCache
        {
            get => CleanerItems.Contains("RobloxStudioCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxStudioCache");
                else
                    CleanerItems.Remove("RobloxStudioCache");
            }
        }

        public bool CleanerCatstrap
        {
            get => CleanerItems.Contains("CatstrapLogs");
            set
            {
                if (value)
                    CleanerItems.Add("CatstrapLogs");
                else
                    CleanerItems.Remove("CatstrapLogs");
            }
        }
    }
}
