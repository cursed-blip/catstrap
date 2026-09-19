using Bloxstrap.Models.APIs.RoValra;

namespace Bloxstrap.Integrations
{
    public static class MatchmakingRegions
    {
        public const string AutoRegion = "Auto";

        private const string LOG_IDENT = "MatchmakingRegions";

        private static readonly Uri DatacentersUrl = new("https://apis.rovalra.com/v1/datacenters/list");

        private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromHours(24);

        private static readonly TimeSpan DiskCacheLifetime = TimeSpan.FromDays(7);

        private static List<RoValraDatacenter>? _entries;

        private static DateTime _fetchedAt = DateTime.MinValue;

        private static Task<List<RoValraDatacenter>>? _inFlight;

        private static string CachePath => Path.Combine(Paths.Base, "DatacentersCache.json");

        public static string BuildRegionKey(string? city, string? country)
        {
            if (String.IsNullOrWhiteSpace(city) && String.IsNullOrWhiteSpace(country))
                return "Unknown";

            if (String.IsNullOrWhiteSpace(city))
                return country!.Trim();

            if (String.IsNullOrWhiteSpace(country))
                return city!.Trim();

            return $"{city.Trim()}, {country.Trim()}";
        }

        public static bool TryParseRegion(string? region, out string? city, out string? country)
        {
            city = null;
            country = null;

            if (String.IsNullOrWhiteSpace(region))
                return false;

            string[] parts = region.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 2)
            {
                city = parts[0];
                country = parts[1];
                return true;
            }

            if (parts.Length == 1)
            {
                country = parts[0];
                return true;
            }

            return false;
        }

        public static bool IsAuto(string? region) =>
            String.IsNullOrWhiteSpace(region) || region.Equals(AutoRegion, StringComparison.OrdinalIgnoreCase);

        public static Task<List<RoValraDatacenter>> GetDatacentersAsync()
        {
            List<RoValraDatacenter>? entries = GetMemoryCache();

            if (entries is not null)
                return Task.FromResult(entries);

            return _inFlight ??= FetchDatacentersAsync();
        }

        private static List<RoValraDatacenter>? GetMemoryCache()
        {
            List<RoValraDatacenter>? entries = _entries;

            if (entries is null || DateTime.UtcNow - _fetchedAt >= MemoryCacheLifetime)
                return null;

            return entries;
        }

        private static async Task<List<RoValraDatacenter>> FetchDatacentersAsync()
        {
            try
            {
                List<RoValraDatacenter>? cached = await LoadFromDiskAsync();

                if (cached is not null)
                {
                    _entries = cached;
                    _fetchedAt = DateTime.UtcNow;
                    return cached;
                }

                try
                {
                    List<RoValraDatacenter>? fresh = await Http.GetJson<List<RoValraDatacenter>>(DatacentersUrl);

                    if (fresh is not null && fresh.Count > 0)
                    {
                        _entries = fresh;
                        _fetchedAt = DateTime.UtcNow;
                        await SaveToDiskAsync(fresh);
                        return fresh;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to list datacenter regions: {ex.Message}");
                }

                List<RoValraDatacenter>? stale = await LoadFromDiskAsync(allowExpired: true);

                if (stale is not null)
                {
                    _entries = stale;
                    _fetchedAt = DateTime.UtcNow;
                    return stale;
                }

                return new List<RoValraDatacenter>();
            }
            finally
            {
                _inFlight = null;
            }
        }

        public static async Task<List<string>> GetRegionNamesAsync()
        {
            List<RoValraDatacenter> entries = await GetDatacentersAsync().ConfigureAwait(false);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(entries.Count);

            foreach (RoValraDatacenter entry in entries)
            {
                string key = BuildRegionKey(entry.Location?.City, entry.Location?.Country);

                if (key == "Unknown" || !names.Add(key))
                    continue;

                result.Add(key);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);

            return result;
        }

        public static IEnumerable<Uri> BuildServerUrls(long? placeId, string? city, string? country)
        {
            if (!String.IsNullOrWhiteSpace(city) && !String.IsNullOrWhiteSpace(country))
                yield return new Uri($"https://apis.rovalra.com/v1/servers/region?place_id={placeId}&country={country}&city={Uri.EscapeDataString(city)}");

            if (!String.IsNullOrWhiteSpace(country))
                yield return new Uri($"https://apis.rovalra.com/v1/servers/region?place_id={placeId}&region={country}");
        }

        private static async Task<List<RoValraDatacenter>?> LoadFromDiskAsync(bool allowExpired = false)
        {
            try
            {
                if (!Paths.Initialized)
                    return null;

                string path = CachePath;

                if (!File.Exists(path))
                    return null;

                string json = await File.ReadAllTextAsync(path);

                DatacentersCache? cache = JsonSerializer.Deserialize<DatacentersCache>(json);

                if (cache is null || !cache.Datacenters.Any())
                    return null;

                if (!allowExpired && DateTime.UtcNow - cache.LastUpdated.ToUniversalTime() > DiskCacheLifetime)
                    return null;

                return cache.Datacenters;
            }
            catch
            {
                return null;
            }
        }

        private static async Task SaveToDiskAsync(List<RoValraDatacenter> entries)
        {
            try
            {
                if (!Paths.Initialized)
                    return;

                var cache = new DatacentersCache
                {
                    Datacenters = entries,
                    LastUpdated = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(cache);

                await File.WriteAllTextAsync(CachePath, json);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to cache datacenter regions: {ex.Message}");
            }
        }
    }
}
