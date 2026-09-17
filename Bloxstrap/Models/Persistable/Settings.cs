using DiscordRPC;
using System.Collections.ObjectModel;

namespace Bloxstrap.Models.Persistable
{
    public class Settings
    {
        // uh
        public bool AllowCookieAccess { get; set; } = false;

        // bloxstrap configuration
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentAeroDialog;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconBloxstrap;
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public RobloxIcon RobloxIcon { get; set; } = RobloxIcon.IconDefault;
        public string RobloxTitle { get; set; } = "Roblox";
        public string RobloxIconCustomLocation { get; set; } = "";
        public Theme Theme { get; set; } = Theme.Default;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DeveloperMode { get; set; } = false;
        // PLEASE DONT FORGET TO TURN THIS OFF !!!!
        public bool UseAcrylicBackground { get; set; } = false;
        public byte AcrylicBackgroundOpacity { get; set; } = 165;
        public bool ForceLocalData { get; set; } = true;
        public bool CheckForUpdates { get; set; } = true;
        public bool ConfirmLaunches { get; set; } = true;
        public string Locale { get; set; } = "nil";
        public bool ForceRobloxLanguage { get; set; } = false;
        public bool UseFastFlagManager { get; set; } = true;
        public bool WPFSoftwareRender { get; set; } = false;
        public bool EnableAnalytics { get; set; } = false;
        public bool StaticDirectory { get; set; } = false;
        public string Channel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public string RobloxDomain { get; set; } = RobloxInterfaces.Deployment.DefaultRobloxDomain;
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Automatic;
        public string? SelectedCustomTheme { get; set; } = null;
        public bool BackgroundUpdatesEnabled { get; set; } = false;
        public bool DebugDisableVersionPackageCleanup { get; set; } = false;
        public bool EnableBetterMatchmaking { get; set; } = false;
        public bool EnableBetterMatchmakingRandomization { get; set; } = false;
        public WebEnvironment WebEnvironment { get; set; } = WebEnvironment.Production;

        // integration configuration
        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.TwoWeeks;
        // how do i automate this? -Naveandice
        public List<string> CleanerDirectories { get; set; } = new List<string> {
            "RobloxCache",
            "RobloxStudioCache",
            "RobloxLogs",
            "CatstrapLogs"
        };
        public bool EnableWindowManipulation { get; set; } = false;
        public bool FakeBorderlessFullscreen { get; set; } = false;
        public bool EnableActivityTracking { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public DiscordRPCStatusDisplay RichPresenceStatusDisplayType { get; set; } = DiscordRPCStatusDisplay.Name;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;
        public bool ShowServerDetails { get; set; } = false;
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = new();

        // mod preset configuration
        public bool UseDisableAppPatch { get; set; } = false;

        public bool EnableAssetProxy { get; set; } = false;

        public AssetProxyCacheRefresh AssetProxyCacheRefresh { get; set; } = AssetProxyCacheRefresh.OnRuleChange;
        public bool CacheOriginalAssets { get; set; } = true;

        public ObservableCollection<AssetProxyRule> AssetProxyRules { get; set; } = new();

        public ObservableCollection<AssetProxyConfig> AssetProxyConfigs { get; set; } = new();

        public ObservableCollection<QuickplayGame> QuickplayGames { get; set; } = new();

        public bool TrackRecentlyPlayedGames { get; set; } = true;

        public bool ShowLivePlayerCounts { get; set; } = true;

        public bool ShowServerRegions { get; set; } = true;

        public int QuickplayIconPrefetch { get; set; } = 12;

        public int ThumbnailCacheLimitMb { get; set; } = 64;

        public bool ReduceVisualEffects { get; set; } = false;

        public PinnedClientVersion? PinnedVersion { get; set; } = null;

        public bool BlockRobloxTelemetry { get; set; } = true;

        public string AccentColor { get; set; } = "#B2703E";

        public bool AutoSaveAccounts { get; set; } = true;

        public string BackgroundGifUrl { get; set; } = "";

        public double BackgroundGifOpacity { get; set; } = 0.35;

        public bool PrivacyDefaultsApplied { get; set; } = false;

        public void ApplyPrivacyDefaults()
        {
            if (PrivacyDefaultsApplied)
                return;

            ForceLocalData = true;
            BlockRobloxTelemetry = true;
            PrivacyDefaultsApplied = true;

            App.Logger.WriteLine("Settings::ApplyPrivacyDefaults", "Applied privacy-first defaults (Roblox telemetry blocked, no remote data fetch)");
        }

        public void MigrateAssetProxyRules()
        {
            if (AssetProxyRules.Count == 0)
                return;

            AssetProxyConfigs.Add(new AssetProxyConfig
            {
                Name = "Imported rules",
                Rules = AssetProxyRules
            });

            AssetProxyRules = new();
        }
    }
}
