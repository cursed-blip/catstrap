using System.Collections.Concurrent;

using Bloxstrap.Models.APIs.RoValra;
using Bloxstrap.Models.Entities;

namespace Bloxstrap.Integrations
{
    public static class QuickplayRegions
    {
        private const string LOG_IDENT = "QuickplayRegions";

        private const int MaxRegions = 12;

        private const int MaxConcurrency = 6;

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan DatacenterCacheLifetime = TimeSpan.FromHours(24);

        private static readonly ConcurrentDictionary<long, RegionSample> _cache = new();

        private static readonly SemaphoreSlim _countriesLock = new(1, 1);

        private static List<string>? _countries;
        private static DateTime _countriesFetchedAt = DateTime.MinValue;

        private class RegionSample
        {
            public DateTime FetchedAt { get; set; }

            public string Summary { get; set; } = "";
        }

        public static bool HasFreshSample(long placeId) =>
            _cache.TryGetValue(placeId, out RegionSample? sample) && DateTime.Now - sample.FetchedAt < CacheLifetime;

        public static async Task<string> GetSummaryAsync(long placeId, bool forceRefresh = false)
        {
            if (placeId <= 0)
                return "";

            if (!forceRefresh && _cache.TryGetValue(placeId, out RegionSample? cached) && DateTime.Now - cached.FetchedAt < CacheLifetime)
                return cached.Summary;

            string summary = await SampleAsync(placeId);

            _cache[placeId] = new RegionSample
            {
                FetchedAt = DateTime.Now,
                Summary = summary
            };

            return summary;
        }

        private static async Task<string> SampleAsync(long placeId)
        {
            List<string> countries = await GetCountriesAsync();

            if (!countries.Any())
                return "";

            var counts = new ConcurrentDictionary<string, int>();
            var semaphore = new SemaphoreSlim(MaxConcurrency);

            var tasks = countries.Select(async country =>
            {
                await semaphore.WaitAsync();

                try
                {
                    Uri url = new($"https://apis.rovalra.com/v1/servers/region?place_id={placeId}&region={country}");
                    RoValraServers? response = await Http.GetJson<RoValraServers>(url);

                    int servers = response?.Servers?.Count ?? 0;

                    if (servers > 0)
                        counts[country] = servers;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"No server data for {country}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (!counts.Any())
                return "";

            int total = counts.Values.Sum();
            int sampled = counts.Count;

            var ranked = counts.OrderByDescending(x => x.Value).ToList();

            string summary = String.Join("  ·  ", ranked.Take(3).Select(x => $"{x.Key} {x.Value * 100 / total}%"));

            if (sampled > 3)
                summary += $"  +{sampled - 3} more";

            App.Logger.WriteLine(LOG_IDENT, $"Place {placeId}: {total} servers across {sampled} sampled regions");

            return summary;
        }

        private static async Task<List<string>> GetCountriesAsync()
        {
            await _countriesLock.WaitAsync();

            try
            {
                if (_countries is not null && DateTime.Now - _countriesFetchedAt < DatacenterCacheLifetime)
                    return _countries;

                Uri url = new("https://apis.rovalra.com/v1/datacenters/list");
                List<RoValraDatacenter>? datacenters = await Http.GetJson<List<RoValraDatacenter>>(url);

                if (datacenters is null || !datacenters.Any())
                    return _countries ?? new List<string>();

                _countries = datacenters
                    .Where(x => !String.IsNullOrWhiteSpace(x.Location?.Country))
                    .GroupBy(x => x.Location.Country.Trim().ToUpperInvariant())
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Take(MaxRegions)
                    .ToList();

                _countriesFetchedAt = DateTime.Now;

                return _countries;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to list datacenter regions: {ex.Message}");

                return _countries ?? new List<string>();
            }
            finally
            {
                _countriesLock.Release();
            }
        }

        public static void RestoreFromLibrary()
        {
            foreach (QuickplayGame game in Quickplay.Games)
            {
                if (!game.HasRegionSummary)
                    continue;

                _cache[game.PlaceId] = new RegionSample
                {
                    FetchedAt = game.RegionsFetchedAt,
                    Summary = game.RegionSummary
                };
            }
        }
    }
}
