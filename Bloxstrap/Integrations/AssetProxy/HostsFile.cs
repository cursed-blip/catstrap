using System.Security.Principal;
using System.Text;

namespace Bloxstrap.Integrations.AssetProxy
{
    internal static class HostsFile
    {
        public const string Marker = "# Catstrap asset proxy entry";

        private const string EndMarker = "# end Catstrap asset proxy entry";

        private static string HostsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts"
        );

        public static bool IsElevated
        {
            get
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool AreEntriesPresent()
        {
            try
            {
                return File.Exists(HostsPath) && File.ReadAllText(HostsPath).Contains(Marker, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static bool Apply(IEnumerable<string> hosts, out string error)
        {
            error = "";

            var wanted = hosts.Where(host => !string.IsNullOrWhiteSpace(host)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (wanted.Count == 0)
                return true;

            try
            {
                string content = ReadAll();
                string cleaned = StripOurEntries(content);

                var builder = new StringBuilder(cleaned.TrimEnd('\r', '\n'));
                builder.Append("\r\n\r\n").Append(Marker).Append("\r\n");

                foreach (string host in wanted)
                    builder.Append("127.0.0.1 ").Append(host).Append("\r\n");

                builder.Append(EndMarker).Append("\r\n");

                File.WriteAllText(HostsPath, builder.ToString());

                FlushDnsCache();

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Catstrap needs to run as administrator to redirect Roblox's asset hosts";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Remove(out string error)
        {
            error = "";

            try
            {
                if (!File.Exists(HostsPath))
                    return true;

                string content = ReadAll();

                if (!content.Contains(Marker, StringComparison.Ordinal))
                    return true;

                File.WriteAllText(HostsPath, StripOurEntries(content));

                FlushDnsCache();

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Catstrap needs to run as administrator to restore the hosts file";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void FlushDnsCache()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"),
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HostsFile", $"Could not flush the DNS cache: {ex.Message}");
            }
        }

        private static string ReadAll()
        {
            byte[] bytes = File.ReadAllBytes(HostsPath);
            return new UTF8Encoding(false).GetString(bytes);
        }

        private static string StripOurEntries(string content)
        {
            var kept = new List<string>();
            bool insideBlock = false;

            foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.Equals(Marker, StringComparison.Ordinal))
                {
                    insideBlock = true;
                    continue;
                }

                if (trimmed.Equals(EndMarker, StringComparison.Ordinal))
                {
                    insideBlock = false;
                    continue;
                }

                if (insideBlock)
                    continue;

                if (trimmed.StartsWith("127.0.0.1", StringComparison.Ordinal)
                    && AssetProxyManager.InterceptHosts.Any(host => trimmed.EndsWith(host, StringComparison.OrdinalIgnoreCase)))
                    continue;

                kept.Add(line);
            }

            string result = string.Join("\r\n", kept);

            return result.TrimEnd('\r', '\n') + "\r\n";
        }
    }
}
