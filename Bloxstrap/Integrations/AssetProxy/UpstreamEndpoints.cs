using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Bloxstrap.Integrations.AssetProxy
{
    internal static class UpstreamEndpoints
    {
        private const string LOG_IDENT = "UpstreamEndpoints";

        private static readonly TimeSpan UnhealthyFor = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

        private const int MaxAddresses = 12;

        private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(5);

        private static readonly object SyncLock = new();

        private static readonly Dictionary<string, HostEndpoints> Known = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] PublicResolvers = { "1.1.1.1", "8.8.8.8" };

        private static bool _loaded;

        private static string StatePath => Path.Combine(AssetProxyManager.AssetProxyDirectory, "upstream.json");

        private sealed class HostEndpoints
        {
            public List<string> Addresses { get; set; } = new();

            public Dictionary<string, DateTime> UnhealthyUntil { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            public DateTime RefreshedUtc { get; set; } = DateTime.MinValue;

            public DateTime LastRefreshAttemptUtc { get; set; } = DateTime.MinValue;
        }

        public static void ResolveAndStore(IEnumerable<string> hosts)
        {
            lock (SyncLock)
            {
                Load();

                foreach (string host in hosts)
                    Resolve(host, force: true);

                Save();
            }
        }

        public static IReadOnlyList<string> Get(string host)
        {
            lock (SyncLock)
            {
                Load();

                if (!Known.TryGetValue(host, out HostEndpoints? endpoints) || endpoints.Addresses.Count == 0)
                    endpoints = Resolve(host, force: true);

                if (endpoints is null || endpoints.Addresses.Count == 0)
                    return Array.Empty<string>();

                bool allUnhealthy = endpoints.Addresses.All(address => IsUnhealthy(endpoints, address));

                if (allUnhealthy || DateTime.UtcNow - endpoints.RefreshedUtc > RefreshInterval)
                {
                    var refreshed = Resolve(host, force: false);

                    if (refreshed is not null && refreshed.Addresses.Count > 0)
                        endpoints = refreshed;
                }

                var ordered = endpoints.Addresses
                    .OrderBy(address => IsUnhealthy(endpoints, address) ? 1 : 0)
                    .ToList();

                if (ordered.Count != endpoints.Addresses.Count)
                    endpoints.Addresses = ordered;

                return ordered;
            }
        }

        public static void ReportFailure(string host, string? address)
        {
            lock (SyncLock)
            {
                Load();

                if (!Known.TryGetValue(host, out HostEndpoints? endpoints))
                    return;

                if (!string.IsNullOrEmpty(address))
                    endpoints.UnhealthyUntil[address!] = DateTime.UtcNow + UnhealthyFor;

                endpoints.RefreshedUtc = DateTime.MinValue;

                App.Logger.WriteLine(LOG_IDENT, address is null
                    ? $"{host} failed; the next request will re-resolve it"
                    : $"{host} at {address} failed; the next request will try another address");

                Save();
            }
        }

        public static void ReportSuccess(string host, string address)
        {
            lock (SyncLock)
            {
                Load();

                if (!Known.TryGetValue(host, out HostEndpoints? endpoints))
                {
                    endpoints = new HostEndpoints();
                    Known[host] = endpoints;
                }

                endpoints.UnhealthyUntil.Remove(address);
                endpoints.Addresses.RemoveAll(existing => existing.Equals(address, StringComparison.OrdinalIgnoreCase));
                endpoints.Addresses.Insert(0, address);

                if (endpoints.RefreshedUtc == DateTime.MinValue)
                    endpoints.RefreshedUtc = DateTime.UtcNow;
            }
        }

        public static void Forget(string host)
        {
            lock (SyncLock)
            {
                Load();

                if (Known.Remove(host))
                    Save();
            }
        }

        private static bool IsUnhealthy(HostEndpoints endpoints, string address) =>
            endpoints.UnhealthyUntil.TryGetValue(address, out DateTime until) && until > DateTime.UtcNow;

        private static HostEndpoints? Resolve(string host, bool force)
        {
            if (!Known.TryGetValue(host, out HostEndpoints? endpoints))
            {
                endpoints = new HostEndpoints();
                Known[host] = endpoints;
            }

            if (!force && DateTime.UtcNow - endpoints.LastRefreshAttemptUtc < RefreshCooldown)
                return endpoints;

            endpoints.LastRefreshAttemptUtc = DateTime.UtcNow;

            var found = new List<string>();

            var system = SystemResolve(host);

            if (system.Count > 0 && !system.All(IPAddress.IsLoopback))
                found.AddRange(system.Where(address => !IPAddress.IsLoopback(address)).Select(address => address.ToString()));

            if (found.Count == 0)
                found.AddRange(ResolveViaPublicDns(host));

            if (found.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not resolve {host}, keeping the addresses we already had");
                return endpoints;
            }

            var merged = new List<string>(found);

            foreach (string address in endpoints.Addresses)
            {
                if (merged.Count >= MaxAddresses)
                    break;

                if (!merged.Contains(address, StringComparer.OrdinalIgnoreCase))
                    merged.Add(address);
            }

            var kept = new HashSet<string>(merged, StringComparer.OrdinalIgnoreCase);

            foreach (string address in endpoints.UnhealthyUntil.Keys.ToList())
            {
                if (!kept.Contains(address))
                    endpoints.UnhealthyUntil.Remove(address);
            }

            endpoints.Addresses = merged;
            endpoints.RefreshedUtc = DateTime.UtcNow;

            App.Logger.WriteLine(LOG_IDENT, $"{host} resolves to {string.Join(", ", found)}");

            return endpoints;
        }

        private static List<IPAddress> SystemResolve(string host)
        {
            try
            {
                return Dns.GetHostAddresses(host)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Resolving {host} failed: {ex.Message}");
                return new List<IPAddress>();
            }
        }

        private static List<string> ResolveViaPublicDns(string host)
        {
            foreach (string resolver in PublicResolvers)
            {
                try
                {
                    var addresses = Query(resolver, host);

                    if (addresses.Count > 0)
                        return addresses;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Public resolver {resolver} could not answer for {host}: {ex.Message}");
                }
            }

            return new List<string>();
        }

        private static List<string> Query(string resolver, string host)
        {
            var results = new List<string>();
            var query = new List<byte>();
            var random = new Random();

            query.Add((byte)random.Next(0, 256));
            query.Add((byte)random.Next(0, 256));

            query.AddRange(new byte[] { 0x01, 0x00 });
            query.AddRange(new byte[] { 0x00, 0x01 });
            query.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            foreach (string label in host.Split('.'))
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(label);

                query.Add((byte)bytes.Length);
                query.AddRange(bytes);
            }

            query.Add(0x00);
            query.AddRange(new byte[] { 0x00, 0x01 });
            query.AddRange(new byte[] { 0x00, 0x01 });

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 2000;
            udp.Connect(resolver, 53);
            udp.Send(query.ToArray(), query.Count);

            var remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] response = udp.Receive(ref remote);

            int offset = 12;

            while (offset < response.Length && response[offset] != 0)
            {
                if ((response[offset] & 0xC0) == 0xC0)
                {
                    offset += 2;
                    break;
                }

                offset += 1 + response[offset];
            }

            if (offset >= response.Length)
                return results;

            offset += 5;

            if (offset + 12 > response.Length)
                return results;

            int answers = (response[6] << 8) | response[7];

            for (int i = 0; i < answers && offset + 12 <= response.Length; i++)
            {
                if ((response[offset] & 0xC0) == 0xC0)
                    offset += 2;
                else
                {
                    while (offset < response.Length && response[offset] != 0)
                        offset += 1 + response[offset];

                    offset += 1;
                }

                if (offset + 10 > response.Length)
                    break;

                int type = (response[offset] << 8) | response[offset + 1];
                int length = (response[offset + 8] << 8) | response[offset + 9];

                offset += 10;

                if (type == 1 && length == 4 && offset + 4 <= response.Length)
                {
                    string address = new IPAddress(response.Skip(offset).Take(4).ToArray()).ToString();

                    if (!results.Contains(address))
                        results.Add(address);
                }

                offset += length;
            }

            return results;
        }

        private static void Load()
        {
            if (_loaded)
                return;

            _loaded = true;

            try
            {
                if (!File.Exists(StatePath))
                    return;

                var stored = JsonSerializer.Deserialize<Dictionary<string, HostEndpoints>>(File.ReadAllText(StatePath));

                if (stored is null)
                    return;

                foreach (var (host, endpoints) in stored)
                {
                    if (endpoints.Addresses is null)
                        continue;

                    Known[host] = endpoints;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read saved upstream addresses: {ex.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AssetProxyManager.AssetProxyDirectory);

                var snapshot = Known.ToDictionary(entry => entry.Key, entry => entry.Value);

                File.WriteAllText(StatePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not save upstream addresses: {ex.Message}");
            }
        }
    }
}
