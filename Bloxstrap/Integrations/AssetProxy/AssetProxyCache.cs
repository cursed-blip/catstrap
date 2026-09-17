using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bloxstrap.Integrations.AssetProxy
{
    internal static class AssetProxyCache
    {
        private const string LOG_IDENT = "AssetProxyCache";

        private const string StateFileName = "cache-state.json";

        private static string StatePath => Path.Combine(AssetProxyManager.AssetProxyDirectory, StateFileName);

        private class CacheState
        {
            public string Fingerprint { get; set; } = "";
            public string ClearedAt { get; set; } = "";
        }

        public static void RefreshIfNeeded()
        {
            try
            {
                var mode = App.Settings.Prop.AssetProxyCacheRefresh;

                if (mode == AssetProxyCacheRefresh.Never)
                    return;

                if (AssetProxyHost.IsClientRunning())
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox is running, leaving the asset cache alone");
                    return;
                }

                string fingerprint = BuildFingerprint();
                CacheState? state = ReadState();

                if (mode == AssetProxyCacheRefresh.OnRuleChange && state?.Fingerprint == fingerprint)
                    return;

                if (ClearCache(state))
                {
                    WriteState(new CacheState
                    {
                        Fingerprint = fingerprint,
                        ClearedAt = DateTime.UtcNow.ToString("O")
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AssetProxyCache::RefreshIfNeeded", ex);
            }
        }

        private static string BuildFingerprint()
        {
            var builder = new StringBuilder();

            foreach (var config in App.Settings.Prop.AssetProxyConfigs)
            {
                builder.Append(config.Enabled ? '+' : '-').Append(config.Name).Append('|');

                if (!config.Enabled)
                    continue;

                foreach (var rule in config.Rules)
                {
                    builder.Append(rule.Enabled ? '+' : '-')
                        .Append(rule.FromAssetId).Append(':')
                        .Append(rule.Action).Append(':')
                        .Append(rule.ToAssetId).Append(':')
                        .Append(rule.RedirectTarget)
                        .Append('|');
                }
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
        }

        private static bool ClearCache(CacheState? state)
        {
            string[] files =
            {
                Path.Combine(Paths.Roblox, "rbx-storage.db"),
                Path.Combine(Paths.Roblox, "rbx-storage.db-shm"),
                Path.Combine(Paths.Roblox, "rbx-storage.db-wal")
            };

            bool cleared = false;

            foreach (string file in files)
            {
                try
                {
                    if (!File.Exists(file))
                        continue;

                    long size = new FileInfo(file).Length;

                    File.Delete(file);

                    cleared = true;

                    App.Logger.WriteLine(LOG_IDENT, $"Removed {Path.GetFileName(file)} ({size / 1024 / 1024} MB) so swapped assets are fetched again");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not remove {Path.GetFileName(file)}: {ex.Message}");
                    return false;
                }
            }

            if (cleared)
                App.Logger.WriteLine(LOG_IDENT, "Asset cache index cleared" + (state?.Fingerprint is null ? "" : $", previous fingerprint {state.Fingerprint}"));

            return cleared;
        }

        private static CacheState? ReadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return null;

                return JsonSerializer.Deserialize<CacheState>(File.ReadAllText(StatePath));
            }
            catch
            {
                return null;
            }
        }

        private static void WriteState(CacheState state)
        {
            try
            {
                Directory.CreateDirectory(AssetProxyManager.AssetProxyDirectory);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not record the cache state: {ex.Message}");
            }
        }
    }
}
