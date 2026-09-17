using Bloxstrap.Models.Entities;

namespace Bloxstrap.RobloxInterfaces
{
    public static class ClientVersionLibrary
    {
        private const string LOG_IDENT = "ClientVersionLibrary";

        private const int LogScanLimit = 40;

        private const int LogHeadChars = 512 * 1024;

        private static readonly Regex BuildFolderPattern = new(@"^version-[0-9a-fA-F]{16}$", RegexOptions.Compiled);

        private static readonly Regex LogNamePattern = new(@"^(?<version>[0-9][0-9.]+)_(?<timestamp>\d{8}T\d{6}Z)_", RegexOptions.Compiled);

        private static readonly Regex LogBuildPattern = new(@"[\\/](version-[0-9a-fA-F]{16})[\\/]", RegexOptions.Compiled);

        private static IEnumerable<string> VersionRoots
        {
            get
            {
                yield return Paths.Versions;
                yield return Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                yield return Path.Combine(Paths.LocalAppData, "Bloxstrap", "Versions");
                yield return Path.Combine(Paths.LocalAppData, "Fishstrap", "Versions");
            }
        }

        public static async Task<List<ClientVersionEntry>> GetAllAsync(int max = 40)
        {
            var found = new Dictionary<string, ClientVersionEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Dictionary<string, string> knownVersions = ScanPlayerLogs(found);

                AddInstalled(found, knownVersions);
                AddBuildFolders(found, knownVersions);

                foreach (ClientVersionEntry entry in await DeployHistory.GetRecentClientVersionsAsync(max))
                    found.TryAdd(entry.VersionGuid, entry);

                App.Logger.WriteLine(LOG_IDENT, $"Listing {found.Count} builds ({found.Count(x => x.Value.IsLocal)} found on this PC)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to assemble the client version list");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            return found.Values
                .OrderByDescending(x => x.IsInstalled)
                .ThenByDescending(x => x.DeployedAt ?? DateTime.MinValue)
                .Take(max)
                .ToList();
        }

        private static void AddInstalled(Dictionary<string, ClientVersionEntry> found, Dictionary<string, string> knownVersions)
        {
            string guid = DeployHistory.GetInstalledVersionGuid();

            if (String.IsNullOrEmpty(guid))
                return;

            string version = DeployHistory.GetInstalledVersion();

            if (String.IsNullOrEmpty(version) && knownVersions.TryGetValue(guid, out string? known))
                version = known;

            found[guid] = new ClientVersionEntry
            {
                VersionGuid = guid,
                Version = version,
                Source = "installed now",
                IsLocal = true,
                IsInstalled = true,
                DeployedAt = GetInstalledAt(guid)
            };
        }

        private static void AddBuildFolders(Dictionary<string, ClientVersionEntry> found, Dictionary<string, string> knownVersions)
        {
            foreach (string root in VersionRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string[] folders;

                try
                {
                    if (!Directory.Exists(root))
                        continue;

                    folders = Directory.GetDirectories(root);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not list {root}: {ex.Message}");
                    continue;
                }

                foreach (string folder in folders)
                {
                    string guid = Path.GetFileName(folder);

                    if (!BuildFolderPattern.IsMatch(guid))
                        continue;

                    string version = knownVersions.TryGetValue(guid, out string? known) ? known : GetVersionFromInstall(folder);

                    if (found.TryGetValue(guid, out ClientVersionEntry? existing))
                    {
                        if (String.IsNullOrEmpty(existing.Version))
                            existing.Version = version;

                        continue;
                    }

                    found[guid] = new ClientVersionEntry
                    {
                        VersionGuid = guid,
                        Version = version,
                        Source = "on your PC",
                        IsLocal = true,
                        DeployedAt = GetInstallTimestamp(folder)
                    };
                }
            }
        }

        private static Dictionary<string, string> ScanPlayerLogs(Dictionary<string, ClientVersionEntry> found)
        {
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string directory = Paths.RobloxLogs;

            if (!Directory.Exists(directory))
                return versions;

            IEnumerable<FileInfo> logs;

            try
            {
                logs = new DirectoryInfo(directory)
                    .EnumerateFiles("*.log")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(LogScanLimit)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list the player logs: {ex.Message}");
                return versions;
            }

            foreach (FileInfo log in logs)
            {
                Match name = LogNamePattern.Match(log.Name);

                if (!name.Success)
                    continue;

                string version = name.Groups["version"].Value;

                string? guid = FindBuildGuid(ReadHead(log.FullName, LogHeadChars));

                if (guid is null)
                    continue;

                versions.TryAdd(guid, version);

                if (found.ContainsKey(guid))
                    continue;

                found[guid] = new ClientVersionEntry
                {
                    VersionGuid = guid,
                    Version = version,
                    DeployedAt = ParseLogTimestamp(name.Groups["timestamp"].Value),
                    Source = "from your logs",
                    IsLocal = true
                };
            }

            return versions;
        }

        private static string? FindBuildGuid(string text)
        {
            if (String.IsNullOrEmpty(text))
                return null;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in LogBuildPattern.Matches(text))
            {
                string guid = match.Groups[1].Value;

                counts[guid] = (counts.TryGetValue(guid, out int count) ? count : 0) + 1;

                if (!firstSeen.ContainsKey(guid))
                    firstSeen[guid] = match.Index;
            }

            return counts.Keys
                .OrderByDescending(guid => counts[guid])
                .ThenBy(guid => firstSeen[guid])
                .FirstOrDefault();
        }

        private static string ReadHead(string path, int maxChars)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                char[] buffer = new char[maxChars];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);

                return new string(buffer, 0, read);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read {Path.GetFileName(path)}: {ex.Message}");
                return "";
            }
        }

        private static DateTime? ParseLogTimestamp(string value)
        {
            if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                return parsed;

            return null;
        }

        private static string GetVersionFromInstall(string folder)
        {
            try
            {
                string executable = Path.Combine(folder, App.RobloxPlayerAppName);

                if (!File.Exists(executable))
                    return "";

                string? product = FileVersionInfo.GetVersionInfo(executable).ProductVersion;

                return product?.Replace(", ", ".") ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static DateTime? GetInstallTimestamp(string folder)
        {
            try
            {
                string executable = Path.Combine(folder, App.RobloxPlayerAppName);

                if (File.Exists(executable))
                    return File.GetLastWriteTimeUtc(executable);

                return Directory.GetLastWriteTimeUtc(folder);
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? GetInstalledAt(string guid)
        {
            string folder = Path.Combine(Paths.Versions, guid);

            return Directory.Exists(folder) ? GetInstallTimestamp(folder) : null;
        }
    }
}
