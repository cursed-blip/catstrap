using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Bloxstrap.Integrations.AssetProxy
{
    public static class AssetProxyManager
    {
        private const string LOG_IDENT = "AssetProxyManager";

        public const int DefaultPort = 58443;

        public const int OriginPort = 443;

        public static readonly string[] InterceptHosts =
        {
            "assetdelivery.roblox.com",
            "fts.rbxcdn.com",
            "contentdelivery.roblox.com"
        };

        public static bool IsInterceptedHost(string host) => InterceptHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

        private static readonly object SyncLock = new();

        private static readonly object RuleLock = new();

        private static Dictionary<long, AssetProxyRule>? _ruleIndex;

        private static AssetProxyServer? _server;

        private static long _lastHostNotifyTicks;

        private static readonly HttpClient ControlClient = new(
            new HttpClientHandler { UseProxy = false }
        )
        {
            Timeout = TimeSpan.FromMilliseconds(750)
        };

        public static event Action? StopRequested;

        public static IReadOnlyDictionary<long, AssetProxyRule> RuleIndex
        {
            get
            {
                lock (RuleLock)
                {
                    _ruleIndex ??= BuildRuleIndex();

                    return _ruleIndex;
                }
            }
        }

        public static int RuleCount => RuleIndex.Count;

        public static void InvalidateRules(bool notifyHost = true)
        {
            lock (RuleLock)
                _ruleIndex = null;

            if (notifyHost)
                NotifyHost("reload");
        }

        private static Dictionary<long, AssetProxyRule> BuildRuleIndex()
        {
            var index = new Dictionary<long, AssetProxyRule>();

            foreach (AssetProxyConfig config in App.Settings.Prop.AssetProxyConfigs)
            {
                if (!config.Enabled)
                    continue;

                foreach (AssetProxyRule rule in config.Rules)
                {
                    if (!rule.Enabled || rule.FromAssetId <= 0)
                        continue;

                    index[rule.FromAssetId] = rule;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, $"Indexed {index.Count} active asset rules");

            return index;
        }

        #region In-process server (only used by the host process)

        public static bool IsRunning => _server is not null && _server.IsRunning;

        public static bool IsIntercepting => _server is not null && _server.IsOriginRunning;

        public static int Port => _server?.Port ?? 0;

        public static DateTime HostStartedUtc => _server?.StartedAtUtc ?? DateTime.MinValue;

        public static DateTime LastActivityUtc
        {
            get
            {
                if (_server is null)
                    return DateTime.MinValue;

                return _server.LastClientRequestUtc > _server.LastControlRequestUtc
                    ? _server.LastClientRequestUtc
                    : _server.LastControlRequestUtc;
            }
        }

        public static void EnsureStarted(int port = DefaultPort, bool strictPort = false)
        {
            InvalidateRules(notifyHost: false);

            lock (SyncLock)
            {
                if (IsRunning)
                    return;

                try
                {
                    X509Certificate2 caCertificate = LoadOrCreateCaCertificate();
                    PatchRobloxCaCertificates(caCertificate);

                    Directory.CreateDirectory(CacheDirectory);

                    _server = new AssetProxyServer(caCertificate, CacheDirectory)
                    {
                        IsClientRunning = AssetProxyHost.IsClientRunning
                    };

                    _server.StopRequested += () => StopRequested?.Invoke();
                    _server.ReloadRequested += () => InvalidateRules(notifyHost: false);

                    _server.StartOrigin();

                    _server.Start(port, strictPort);

                    App.Logger.WriteLine(LOG_IDENT, $"Asset proxy intercepting on {OriginPort} with control API on {_server.Port}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("AssetProxyManager::EnsureStarted", ex);
                    App.Logger.WriteLine(LOG_IDENT, "Failed to start asset proxy, Roblox will launch without interception");
                    _server = null;
                }
            }
        }

        public static (bool Ok, string? Detail) SelfTest()
        {
            var server = _server;

            if (server is null)
                return (false, "the proxy is not running in this process");

            return server.SelfTestAsync(InterceptHosts[0]).GetAwaiter().GetResult();
        }

        public static void Stop()
        {
            lock (SyncLock)
            {
                if (_server is not null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Stopping asset proxy");
                    _server.Stop();
                    _server.Dispose();
                    _server = null;
                }
            }
        }

        #endregion

        #region Talking to the host process

        public static AssetProxyStatus? GetHostStatus(int port = DefaultPort, int timeoutMs = 750)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);

                string json = ControlClient
                    .GetStringAsync($"http://127.0.0.1:{port}/__catstrap/status", cts.Token)
                    .GetAwaiter()
                    .GetResult();

                var status = JsonSerializer.Deserialize<AssetProxyStatus>(json);

                return status?.App == AssetProxyStatus.Identifier ? status : null;
            }
            catch
            {
                return null;
            }
        }

        public static int EnsureHost(int port = DefaultPort)
        {
            var existing = GetHostStatus(port, 400);

            if (existing is not null)
                return existing.Port;

            string executable = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"-assetproxy {port}",
                    WorkingDirectory = Paths.Base,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                App.Logger.WriteLine(LOG_IDENT, $"Starting the asset proxy host elevated ({executable})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not start the asset proxy host: {ex.Message}");
                return 0;
            }

            for (int waited = 0; waited < 12000; waited += 250)
            {
                Thread.Sleep(250);

                var status = GetHostStatus(port, 400);

                if (status is not null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Asset proxy host is up on port {status.Port} after {waited + 250}ms");
                    return status.Port;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Timed out waiting for the asset proxy host to start");

            return 0;
        }

        public static void RepairHostsEntries()
        {
            try
            {
                if (!HostsFile.AreEntriesPresent())
                    return;

                if (GetHostStatus(DefaultPort, 300) is not null)
                    return;

                if (HostsFile.Remove(out string error))
                    App.Logger.WriteLine(LOG_IDENT, "Removed leftover asset proxy hosts entries");
                else
                    App.Logger.WriteLine(LOG_IDENT, $"Could not remove leftover hosts entries: {error}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AssetProxyManager::RepairHostsEntries", ex);
            }
        }

        public static bool StartElevatedRepair()
        {
            string executable = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-assetproxyrepair",
                    WorkingDirectory = Paths.Base,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not start the repair helper: {ex.Message}");
                return false;
            }
        }

        public static bool StopHost(int port = DefaultPort)
        {
            bool asked = TrySendControl("stop", port);

            Stop();

            return asked;
        }

        private static bool TrySendControl(string command, int port = DefaultPort, int timeoutMs = 400)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);

                using var response = ControlClient
                    .GetAsync($"http://127.0.0.1:{port}/__catstrap/{command}", cts.Token)
                    .GetAwaiter()
                    .GetResult();

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static void NotifyHost(string command)
        {
            long now = DateTime.UtcNow.Ticks;
            long previous = Interlocked.Read(ref _lastHostNotifyTicks);

            if (now - previous < TimeSpan.FromMilliseconds(500).Ticks)
                return;

            Interlocked.Exchange(ref _lastHostNotifyTicks, now);

            _ = Task.Run(() => TrySendControl(command));
        }

        public static long GetRequestCount() => _server?.RequestCount ?? 0;

        public static long GetCachedAssetCount() => _server?.CachedAssets ?? 0;

        #endregion

        public static int GetCacheFileCount()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return 0;

                return Directory.GetFiles(CacheDirectory).Length;
            }
            catch
            {
                return 0;
            }
        }

        public static string AssetProxyDirectory => Path.Combine(Paths.Base, "AssetProxy");

        public static string CacheDirectory => Path.Combine(AssetProxyDirectory, "Cache");

        public static string ConfigsDirectory => Path.Combine(AssetProxyDirectory, "Configs");

        public static string CaCertificatePath => Path.Combine(AssetProxyDirectory, "catstrap-ca.pem");

        public static void PrepareRobloxTrust()
        {
            try
            {
                PatchRobloxCaCertificates(LoadOrCreateCaCertificate());
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AssetProxyManager::PrepareRobloxTrust", ex);
            }
        }

        private static X509Certificate2 LoadOrCreateCaCertificate()
        {
            Directory.CreateDirectory(AssetProxyDirectory);

            if (File.Exists(CaCertificatePath))
            {
                try
                {
                    string pem = File.ReadAllText(CaCertificatePath);

                    if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
                        return LoadCaCertificateFromPem(pem);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to load existing CA certificate, generating a new one: {ex.Message}");
                }
            }

            var certificate = AssetProxyServer.CreateCaCertificate();

            File.WriteAllText(CaCertificatePath, ExportCertificatePem(certificate) + "\n" + ExportRsaPrivateKeyPem(certificate) + "\n");

            App.Logger.WriteLine(LOG_IDENT, "Generated new CA certificate");

            return certificate;
        }

        private const string CaMarker = "# Catstrap Local CA - do not remove";

        private static void PatchRobloxCaCertificates(X509Certificate2 caCertificate)
        {
            string block = $"{CaMarker}\n{ExportCertificatePem(caCertificate)}\n";

            var versionDirectories = new List<string>();

            string stockVersions = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
            if (Directory.Exists(stockVersions))
                versionDirectories.AddRange(Directory.GetDirectories(stockVersions));

            if (Directory.Exists(Paths.Versions))
                versionDirectories.AddRange(Directory.GetDirectories(Paths.Versions));

            foreach (string versionDirectory in versionDirectories.Distinct())
            {
                string cacertPath = Path.Combine(versionDirectory, "ssl", "cacert.pem");

                if (!File.Exists(cacertPath))
                    continue;

                try
                {
                    string content = File.ReadAllText(cacertPath);

                    if (IsUpToDate(content, block))
                        continue;

                    int removed = StripCatstrapCertificates(ref content);

                    File.WriteAllText(cacertPath, content.TrimEnd('\r', '\n') + "\n\n" + block + "\n");

                    App.Logger.WriteLine(LOG_IDENT, removed > 0
                        ? $"Replaced {removed} Catstrap CA entr{(removed == 1 ? "y" : "ies")} in {cacertPath}"
                        : $"Patched {cacertPath}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to patch {cacertPath}: {ex.Message}");
                }
            }
        }

        private static bool IsUpToDate(string content, string block)
        {
            int markerIndex = content.IndexOf(CaMarker, StringComparison.Ordinal);

            if (markerIndex < 0)
                return false;

            if (content.IndexOf(CaMarker, markerIndex + CaMarker.Length, StringComparison.Ordinal) >= 0)
                return false;

            const string blockEnd = "-----END CERTIFICATE-----";

            int endIndex = content.IndexOf(blockEnd, markerIndex, StringComparison.Ordinal);

            if (endIndex < 0)
                return false;

            return content[markerIndex..(endIndex + blockEnd.Length)].Trim() == block.Trim();
        }

        private static int StripCatstrapCertificates(ref string content)
        {
            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";

            content = content.Replace(CaMarker, "", StringComparison.Ordinal);

            var builder = new StringBuilder(content.Length);
            int removed = 0;
            int position = 0;

            while (true)
            {
                int start = content.IndexOf(begin, position, StringComparison.Ordinal);
                int stop = start < 0 ? -1 : content.IndexOf(end, start, StringComparison.Ordinal);

                if (stop < 0)
                {
                    builder.Append(content, position, content.Length - position);
                    break;
                }

                stop += end.Length;

                builder.Append(content, position, start - position);

                string blockText = content[start..stop];

                if (IsCatstrapCertificate(blockText))
                    removed++;
                else
                    builder.Append(blockText);

                position = stop;
            }

            content = builder.ToString();

            return removed;
        }

        private static bool IsCatstrapCertificate(string blockText)
        {
            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";

            try
            {
                int bodyStart = blockText.IndexOf(begin, StringComparison.Ordinal) + begin.Length;
                int bodyEnd = blockText.IndexOf(end, StringComparison.Ordinal);

                byte[] der = Convert.FromBase64String(blockText[bodyStart..bodyEnd].Trim());

                using var certificate = new X509Certificate2(der);

                return certificate.Subject.Contains("Catstrap Local CA", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static X509Certificate2 LoadCaCertificateFromPem(string pem)
        {
            string? certificate = FindPemBlock(pem, "CERTIFICATE");
            string? key = FindPemBlock(pem, "RSA PRIVATE KEY") ?? FindPemBlock(pem, "PRIVATE KEY");

            if (certificate is null || key is null)
                throw new CryptographicException("The saved CA file is not a valid PEM certificate/key pair.");

            return X509Certificate2.CreateFromPem(certificate, key);
        }

        private static string? FindPemBlock(string pem, string label)
        {
            ReadOnlySpan<char> remaining = pem.AsSpan();

            while (PemEncoding.TryFind(remaining, out PemFields fields))
            {
                if (remaining[fields.Label].SequenceEqual(label))
                    return remaining[fields.Location].ToString();

                remaining = remaining.Slice(fields.Location.End.Value);
            }

            return null;
        }

        private static string ExportCertificatePem(X509Certificate2 certificate)
            => new(PemEncoding.Write("CERTIFICATE", certificate.RawData));

        private static string ExportRsaPrivateKeyPem(X509Certificate2 certificate)
        {
            using RSA? rsa = certificate.GetRSAPrivateKey();

            if (rsa is null)
                throw new CryptographicException("The CA certificate does not have an RSA private key.");

            return new(PemEncoding.Write("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey()));
        }
    }
}
