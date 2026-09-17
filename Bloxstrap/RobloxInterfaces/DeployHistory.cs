using Bloxstrap.Models.Entities;

namespace Bloxstrap.RobloxInterfaces
{
    public static class DeployHistory
    {
        private const string LOG_IDENT = "DeployHistory";

        private const string HistoryUrl = "https://setup.rbxcdn.com/DeployHistory.txt";

        private const int TailBytes = 524288;

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        private static List<ClientVersionEntry>? _cache;

        private static readonly string[] PlayerProducts = { "Client", "WindowsPlayer" };

        private const string DatePattern = @"\d{1,2}/\d{1,2}/\d{4} \d{1,2}:\d{2}:\d{2} [AP]M";

        private static readonly Regex EntryPattern = new(
            @"(?:New|Revert)\s+(?<product>[A-Za-z0-9_]+)\s+(?<guid>version-[0-9a-fA-F]{16})\s+at\s+" +
            "(?<date>" + DatePattern + @")(?<tail>[^\r\n]*)",
            RegexOptions.Compiled);

        private static readonly Regex FileVersionPattern = new(@"file ver(?:s)?ion:\s*(?<version>[0-9][0-9,\s]*)", RegexOptions.Compiled);

        private static readonly Regex GitHashPattern = new(@"git hash:\s*(?<version>[0-9][0-9.]*)", RegexOptions.Compiled);

        private static readonly Regex HiddenPattern = new(
            @"(?:New|Revert)\s+(?:Client|WindowsPlayer)\s+version-hidden\s+at\s+(?<date>" + DatePattern + ")",
            RegexOptions.Compiled);

        private static string CachePath => Path.Combine(Paths.Base, "Cache", "DeployHistory-v2.txt");

        public static int HiddenBuildCount { get; private set; }

        public static DateTime? HiddenSince { get; private set; }

        public static async Task<List<ClientVersionEntry>> GetRecentClientVersionsAsync(int count = 20)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (_cache is null)
                    _cache = await LoadAsync();

                return _cache.Take(count).ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to read Roblox's deploy history");
                App.Logger.WriteException(LOG_IDENT, ex);

                return new List<ClientVersionEntry>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static async Task<bool> IsWindowsPlayerBuildAsync(string versionGuid)
        {
            if (String.IsNullOrWhiteSpace(versionGuid))
                return false;

            if (!versionGuid.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                versionGuid = "version-" + versionGuid;

            try
            {
                if (String.IsNullOrEmpty(Deployment.BaseUrl))
                    await Deployment.InitializeConnectivity();

                string manifest = await App.HttpClient.GetStringAsync(Deployment.GetLocation($"/{versionGuid}-rbxPkgManifest.txt"));

                return manifest.Contains("RobloxApp.zip", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{versionGuid} does not look installable: {ex.Message}");
                return false;
            }
        }

        public static string GetInstalledVersionGuid() => App.RobloxState.Prop.Player.VersionGuid ?? "";

        public static string GetInstalledVersion() => Utilities.GetRobloxVersionStr(false);

        private static async Task<List<ClientVersionEntry>> LoadAsync()
        {
            string? cached = await ReadCacheFileAsync();

            if (cached is null)
            {
                cached = await DownloadAsync();

                if (cached is not null && Parse(cached).Count > 0)
                    await WriteCacheFileAsync(cached);
            }

            List<ClientVersionEntry> entries = Parse(cached ?? ReadStaleCache() ?? "");

            if (entries.Count == 0 && File.Exists(CachePath))
            {
                entries = Parse(ReadStaleCache() ?? "");
            }

            App.Logger.WriteLine(LOG_IDENT, $"Found {entries.Count} player builds in Roblox's deploy log, {HiddenBuildCount} of them have a hidden hash");

            return entries;
        }

        private static async Task<string?> DownloadAsync()
        {
            App.Logger.WriteLine(LOG_IDENT, "Fetching the tail of Roblox's deploy history");

            using var request = new HttpRequestMessage(HttpMethod.Get, HistoryUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, TailBytes);

            using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            if (response.StatusCode != HttpStatusCode.PartialContent)
                App.Logger.WriteLine(LOG_IDENT, "CDN ignored the range request, taking the whole log");

            string text = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                int firstCompleteLine = text.IndexOf('\n');

                if (firstCompleteLine > 0)
                    text = text[firstCompleteLine..];
            }

            return text;
        }

        private static async Task<string?> ReadCacheFileAsync()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return null;

                if (DateTime.Now - File.GetLastWriteTime(CachePath) > CacheLifetime)
                    return null;

                return await File.ReadAllTextAsync(CachePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the cached deploy history: {ex.Message}");
                return null;
            }
        }

        private static async Task WriteCacheFileAsync(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                await File.WriteAllTextAsync(CachePath, text);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not cache the deploy history: {ex.Message}");
            }
        }

        private static string? ReadStaleCache()
        {
            try
            {
                if (File.Exists(CachePath))
                    return File.ReadAllText(CachePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the cached deploy history: {ex.Message}");
            }

            return null;
        }

        private static List<ClientVersionEntry> Parse(string text)
        {
            HiddenBuildCount = 0;
            HiddenSince = null;

            if (String.IsNullOrWhiteSpace(text))
                return new List<ClientVersionEntry>();

            var entries = new Dictionary<string, ClientVersionEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in EntryPattern.Matches(text))
            {
                string product = match.Groups["product"].Value;

                if (!PlayerProducts.Any(x => x.Equals(product, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string guid = match.Groups["guid"].Value;

                DateTime? deployedAt = ParseDate(match.Groups["date"].Value);

                if (entries.TryGetValue(guid, out ClientVersionEntry? existing))
                {
                    if (existing.DeployedAt is null && deployedAt is not null)
                        existing.DeployedAt = deployedAt;

                    continue;
                }

                entries[guid] = new ClientVersionEntry
                {
                    VersionGuid = guid,
                    Version = ExtractVersion(match.Groups["tail"].Value),
                    DeployedAt = deployedAt,
                    Source = "Roblox deploy log"
                };
            }

            foreach (Match match in HiddenPattern.Matches(text))
            {
                HiddenBuildCount++;

                DateTime? when = ParseDate(match.Groups["date"].Value);

                if (when is not null && (HiddenSince is null || when < HiddenSince))
                    HiddenSince = when;
            }

            return entries.Values.OrderByDescending(x => x.DeployedAt ?? DateTime.MinValue).ToList();
        }

        private static string ExtractVersion(string tail)
        {
            if (String.IsNullOrEmpty(tail))
                return "";

            Match fileVersion = FileVersionPattern.Match(tail);

            if (fileVersion.Success)
            {
                string value = Regex.Replace(fileVersion.Groups["version"].Value.Trim(), @"[,\s]+", ".").Trim('.');

                if (Regex.IsMatch(value, @"^\d+(\.\d+)+$"))
                    return value;
            }

            Match gitHash = GitHashPattern.Match(tail);

            return gitHash.Success ? gitHash.Groups["version"].Value.TrimEnd('.') : "";
        }

        private static DateTime? ParseDate(string value)
        {
            if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                return parsed;

            return null;
        }
    }
}
