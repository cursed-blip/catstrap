using System.Diagnostics;

namespace Bloxstrap.Integrations.AssetProxy
{
    internal static class AssetProxyHost
    {
        private const string LOG_IDENT = "AssetProxyHost";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan UnusedTimeout = TimeSpan.FromMinutes(5);

        private static volatile bool _stopRequested;

        private static bool _hostsApplied;

        public static void Run(int port, bool manageHosts = true)
        {
            App.Logger.WriteLine(LOG_IDENT, $"Starting on port {port} (elevated: {HostsFile.IsElevated}, managing the hosts file: {manageHosts})");

            if (!HostsFile.IsElevated)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not running elevated, so the hosts file and port 443 are out of reach; stopping");
                return;
            }

            UpstreamEndpoints.ResolveAndStore(AssetProxyManager.InterceptHosts);

            if (manageHosts)
            {
                if (!HostsFile.Apply(AssetProxyManager.InterceptHosts, out string hostsError))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not redirect the asset hosts: {hostsError}");
                    return;
                }

                _hostsApplied = true;

                AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveHostsEntries();
                AppDomain.CurrentDomain.UnhandledException += (_, _) => RemoveHostsEntries();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Diagnostic mode: serving the interception port without touching the hosts file");
            }

            AssetProxyManager.EnsureStarted(port, strictPort: true);

            if (!AssetProxyManager.IsRunning || !AssetProxyManager.IsIntercepting)
            {
                App.Logger.WriteLine(LOG_IDENT, "The listener did not come up, undoing the hosts file change");
                AssetProxyManager.Stop();
                RemoveHostsEntries();
                return;
            }

            var selfTest = AssetProxyManager.SelfTest();

            if (!selfTest.Ok)
            {
                App.Logger.WriteLine(LOG_IDENT, $"A client cannot complete a TLS handshake with the proxy ({selfTest.Detail}), undoing the hosts file change so the game keeps its assets");
                AssetProxyManager.Stop();
                RemoveHostsEntries();
                return;
            }

            if (selfTest.Detail is not null)
                App.Logger.WriteLine(LOG_IDENT, $"Handshake self-test: {selfTest.Detail}");
            else
                App.Logger.WriteLine(LOG_IDENT, "Handshake self-test passed: TLS 1.2, http/1.1, certificate trusted");

            AssetProxyManager.StopRequested += () => _stopRequested = true;

            App.Logger.WriteLine(LOG_IDENT, $"Intercepting for {string.Join(", ", AssetProxyManager.InterceptHosts)}");

            DateTime started = DateTime.UtcNow;
            DateTime lastActivity = started;
            bool clientSeen = false;

            while (!_stopRequested)
            {
                Thread.Sleep(PollInterval);

                bool robloxRunning = IsClientRunning();
                clientSeen |= robloxRunning;

                DateTime activity = AssetProxyManager.LastActivityUtc;

                if (activity > lastActivity)
                    lastActivity = activity;

                if (robloxRunning)
                    lastActivity = DateTime.UtcNow;

                TimeSpan idle = DateTime.UtcNow - lastActivity;

                if (clientSeen && idle > IdleTimeout)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox is gone and nothing has asked for the proxy, shutting down");
                    break;
                }

                if (!clientSeen && DateTime.UtcNow - started > UnusedTimeout && idle > IdleTimeout)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No client ever used the proxy, shutting down");
                    break;
                }
            }

            AssetProxyManager.Stop();
            RemoveHostsEntries();

            App.Logger.WriteLine(LOG_IDENT, "Stopped");
        }

        public static void RemoveHostsEntries()
        {
            if (!_hostsApplied)
                return;

            _hostsApplied = false;

            if (HostsFile.Remove(out string error))
                App.Logger.WriteLine(LOG_IDENT, "Removed the asset proxy hosts entries");
            else
                App.Logger.WriteLine(LOG_IDENT, $"Could not remove the hosts entries: {error}");
        }

        public static bool IsClientRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("RobloxPlayerBeta");

                bool running = processes.Length > 0;

                foreach (var process in processes)
                    process.Dispose();

                return running;
            }
            catch
            {
                return false;
            }
        }
    }
}
