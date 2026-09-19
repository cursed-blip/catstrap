using Bloxstrap.RobloxInterfaces;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Bloxstrap.Models.Entities;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ChannelViewModel : NotifyPropertyChangedViewModel
    {
        public bool IsRobloxInstallationMissing => String.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid) && String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);

        private readonly RelayCommand _pinInstalledVersionCommand;
        private readonly AsyncRelayCommand _pinSelectedVersionCommand;
        private readonly RelayCommand _unpinVersionCommand;
        private readonly AsyncRelayCommand _loadVersionsCommand;

        public ChannelViewModel()
        {
            _pinInstalledVersionCommand = new RelayCommand(PinInstalledVersion);
            _pinSelectedVersionCommand = new AsyncRelayCommand(PinSelectedVersionAsync);
            _unpinVersionCommand = new RelayCommand(UnpinVersion);
            _loadVersionsCommand = new AsyncRelayCommand(LoadRecentVersionsAsync);

            Task.Run(() => LoadChannelDeployInfo(App.Settings.Prop.Channel));
            Task.Run(() => LoadRecentVersionsAsync());
        }

        private bool ValidateDomain(string domain)
        {
            const string domainPattern = @"^([a-zA-Z0-9.-]+)\.([a-zA-Z0-9]+)$";

            return Regex.IsMatch(domain, domainPattern);
        }

        private async Task LoadChannelDeployInfo(string channel)
        {
            ShowLoadingError = false;
            OnPropertyChanged(nameof(ShowLoadingError));

            ChannelInfoLoadingText = Strings.Menu_Channel_Switcher_Fetching;
            OnPropertyChanged(nameof(ChannelInfoLoadingText));

            ChannelDeployInfo = null;
            OnPropertyChanged(nameof(ChannelDeployInfo));

            try
            {
                bool isPrivate = await Deployment.IsChannelPrivate(channel);
                if (App.Cookies.Loaded && isPrivate && string.IsNullOrEmpty(Deployment.ChannelToken))
                {
                    UserChannel? userChannel = await Deployment.GetUserChannel("WindowsPlayer");

                    if (userChannel?.Token is not null)
                        Deployment.ChannelToken = userChannel.Token;
                }

                ClientVersion info = await Deployment.GetInfo(channel, true, true);

                ShowChannelWarning = info.IsBehindDefaultChannel;
                OnPropertyChanged(nameof(ShowChannelWarning));

                ChannelDeployInfo = new DeployInfo
                {
                    Version = info.Version,
                    VersionGuid = isPrivate ? "version-private" : info.VersionGuid, // we dont want to return the hash of private channels for obvious reason
                    Timestamp = info.Timestamp?.ToLocalTime().ToString() ?? "?"
                };

                App.State.Prop.IgnoreOutdatedChannel = true;

                OnPropertyChanged(nameof(ChannelDeployInfo));
            }
            catch (InvalidChannelException ex)
            {
                ShowLoadingError = true;
                OnPropertyChanged(nameof(ShowLoadingError));

                // channels that dont exist also throw HttpStatusCode.Unauthorized
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                    ChannelInfoLoadingText = Strings.Menu_Channel_Switcher_Unauthorized;
                else
                    ChannelInfoLoadingText = $"An http error has occured ({ex.StatusCode})"; // i dont think we need strings for errors

                OnPropertyChanged(nameof(ChannelInfoLoadingText));
            }
        }

        public bool ShowLoadingError { get; set; } = false;
        public bool ShowChannelWarning { get; set; } = false;

        public DeployInfo? ChannelDeployInfo { get; private set; } = null;
        public string ChannelInfoLoadingText { get; private set; } = null!;

        public string ViewChannel
        {
            get => App.Settings.Prop.Channel;
            set
            {
                value = value.Trim();
                Task.Run(() => LoadChannelDeployInfo(value));

                if (value.Equals("live", StringComparison.OrdinalIgnoreCase) || value.Equals("zlive", StringComparison.OrdinalIgnoreCase))
                {
                    App.Settings.Prop.Channel = Deployment.DefaultChannel;
                } else {
                    App.Settings.Prop.Channel = value;
                }
            }
        }

        public bool UpdateCheckingEnabled
        {
            get => App.Settings.Prop.CheckForUpdates;
            set => App.Settings.Prop.CheckForUpdates = value;
        }

        public bool StaticDirectory
        {
            get => App.Settings.Prop.StaticDirectory;
            set => App.Settings.Prop.StaticDirectory = value;
        }

        public string RobloxDomain
        {
            get => App.Settings.Prop.RobloxDomain;
            set
            {
                if (value.Equals("libstanpreg.so", StringComparison.OrdinalIgnoreCase))
                    Frontend.ShowMessageBox("libstanpreg.so is real.", MessageBoxImage.Hand);

                if (ValidateDomain(value))
                    App.Settings.Prop.RobloxDomain = value;
                else
                    Frontend.ShowMessageBox(Strings.Menu_Channel_RobloxDomain_InvalidDomain, MessageBoxImage.Warning, MessageBoxButton.OK);
            }
        }

        public IReadOnlyDictionary<string, ChannelChangeMode> ChannelChangeModes => new Dictionary<string, ChannelChangeMode>
        {
            { Strings.Menu_Channel_ChangeAction_Automatic, ChannelChangeMode.Automatic },
            { Strings.Menu_Channel_ChangeAction_Prompt, ChannelChangeMode.Prompt },
            { Strings.Menu_Channel_ChangeAction_Ignore, ChannelChangeMode.Ignore },
        };

        public string SelectedChannelChangeMode
        {
            get => ChannelChangeModes.FirstOrDefault(x => x.Value == App.Settings.Prop.ChannelChangeMode).Key;
            set => App.Settings.Prop.ChannelChangeMode = ChannelChangeModes[value];
        }

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set => App.State.Prop.ForceReinstall = value;
        }

        #region Version pinning

        public ObservableCollection<ClientVersionEntry> RecentVersions { get; } = new();

        private ClientVersionEntry? _selectedRecentVersion;

        public ClientVersionEntry? SelectedRecentVersion
        {
            get => _selectedRecentVersion;
            set
            {
                _selectedRecentVersion = value;
                OnPropertyChanged(nameof(SelectedRecentVersion));
            }
        }

        private bool _isLoadingVersions;
        private string _versionStatus = "";

        public bool IsLoadingVersions
        {
            get => _isLoadingVersions;
            private set
            {
                _isLoadingVersions = value;
                OnPropertyChanged(nameof(IsLoadingVersions));
            }
        }

        public string VersionStatus
        {
            get => _versionStatus;
            private set
            {
                _versionStatus = value;
                OnPropertyChanged(nameof(VersionStatus));
                OnPropertyChanged(nameof(HasVersionStatus));
            }
        }

        public bool HasVersionStatus => !String.IsNullOrWhiteSpace(_versionStatus);

        private string _versionListNote = "";

        public string VersionListNote
        {
            get => _versionListNote;
            private set
            {
                _versionListNote = value;
                OnPropertyChanged(nameof(VersionListNote));
                OnPropertyChanged(nameof(HasVersionListNote));
            }
        }

        public bool HasVersionListNote => !String.IsNullOrWhiteSpace(_versionListNote);

        public bool IsVersionPinned => App.Settings.Prop.PinnedVersion is not null;

        public string InstalledVersionText
        {
            get
            {
                string guid = DeployHistory.GetInstalledVersionGuid();

                if (String.IsNullOrEmpty(guid))
                    return "Roblox is not installed yet";

                string version = DeployHistory.GetInstalledVersion();

                return String.IsNullOrEmpty(version) ? guid : $"{version} ({guid})";
            }
        }

        public string PinnedVersionText
        {
            get
            {
                PinnedClientVersion? pin = App.Settings.Prop.PinnedVersion;

                if (pin is null)
                    return "Following the channel - always the newest build";

                string pinned = String.IsNullOrEmpty(pin.Version) ? pin.VersionGuid : $"{pin.Version} ({pin.VersionGuid})";

                return $"Pinned to {pinned} since {pin.PinnedAt:yyyy-MM-dd}";
            }
        }

        public string PinWarningText => Deployment.IsDefaultChannel || App.Settings.Prop.PinnedVersion is null
            ? ""
            : $"Pinning only works on the {Deployment.DefaultChannel} channel - your channel is '{Deployment.Channel}', so this pin will not resolve.";

        public bool HasPinWarning => !String.IsNullOrEmpty(PinWarningText);

        public ICommand PinInstalledVersionCommand => _pinInstalledVersionCommand;

        public ICommand PinSelectedVersionCommand => _pinSelectedVersionCommand;

        public ICommand UnpinVersionCommand => _unpinVersionCommand;

        public ICommand LoadVersionsCommand => _loadVersionsCommand;

        private async Task LoadRecentVersionsAsync()
        {
            IsLoadingVersions = true;

            void Populate(List<ClientVersionEntry> versions)
            {
                SelectedRecentVersion = null;
                RecentVersions.Clear();

                foreach (ClientVersionEntry entry in versions)
                    RecentVersions.Add(entry);

                IsLoadingVersions = false;

                if (versions.Count == 0)
                    VersionListNote = "No builds found yet - launch Roblox through Catstrap once and they will show up here.";
                else if (DeployHistory.HiddenSince is null)
                    VersionListNote = "";
                else
                    VersionListNote = "Roblox doesn't publish build ids for its newest builds, so those only appear here once this PC has run them.";
            }

            List<ClientVersionEntry> versions = await ClientVersionLibrary.GetAllAsync(40);

            Dispatcher? dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.CheckAccess())
                Populate(versions);
            else
                dispatcher.Invoke(() => Populate(versions));
        }

        private void PinInstalledVersion()
        {
            string guid = DeployHistory.GetInstalledVersionGuid();

            if (String.IsNullOrEmpty(guid))
            {
                VersionStatus = "Roblox isn't installed yet, so there's nothing to pin.";
                return;
            }

            SetPin(new PinnedClientVersion
            {
                VersionGuid = guid,
                Version = DeployHistory.GetInstalledVersion()
            });

            VersionStatus = $"Pinned to your installed build. Roblox will stop updating until you unpin.";
        }

        private async Task PinSelectedVersionAsync()
        {
            ClientVersionEntry? selected = SelectedRecentVersion;

            if (selected is null)
            {
                VersionStatus = "Pick a version from the list first.";
                return;
            }

            if (!selected.IsLocal)
            {
                VersionStatus = $"Checking that {selected.VersionGuid} is a Windows player build...";

                if (!await DeployHistory.IsWindowsPlayerBuildAsync(selected.VersionGuid))
                {
                    VersionStatus = "That build has no Windows player in it any more - try one of the others.";
                    return;
                }
            }

            SetPin(new PinnedClientVersion
            {
                VersionGuid = selected.VersionGuid,
                Version = selected.Version,
                DeployedAt = selected.DeployedAt
            });

            VersionStatus = $"Pinned to {selected.VersionGuid}. Catstrap will install that build on your next launch.";
        }

        private void SetPin(PinnedClientVersion pin)
        {
            App.Settings.Prop.PinnedVersion = pin;
            App.Settings.Save();

            App.Logger.WriteLine("ChannelViewModel::SetPin", $"Version pinned to {pin.VersionGuid}");

            NotifyVersionState();
        }

        private void UnpinVersion()
        {
            App.Settings.Prop.PinnedVersion = null;
            App.Settings.Save();

            App.Logger.WriteLine("ChannelViewModel::UnpinVersion", "Version pin cleared");

            VersionStatus = "Following the channel again - Roblox will update to the newest build on your next launch.";

            NotifyVersionState();
        }

        private void NotifyVersionState()
        {
            OnPropertyChanged(nameof(IsVersionPinned));
            OnPropertyChanged(nameof(InstalledVersionText));
            OnPropertyChanged(nameof(PinnedVersionText));
            OnPropertyChanged(nameof(PinWarningText));
            OnPropertyChanged(nameof(HasPinWarning));
        }

        #endregion Version pinning
    }
}
