using System.Security.Cryptography;
using System.Text;

namespace Bloxstrap.Integrations
{
    public static class BackgroundGif
    {
        private const string LOG_IDENT = "BackgroundGif";

        private const int MaxFiles = 6;

        public static string CacheDirectory => Path.Combine(Paths.Base, "Background");

        public static string PathFor(string url)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim()));
            string name = Convert.ToHexString(hash)[..20].ToLowerInvariant();

            return Path.Combine(CacheDirectory, name + ".gif");
        }

        public static bool IsCached(string url)
        {
            try
            {
                string path = PathFor(url);

                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<string> EnsureAsync(string url, CancellationToken token = default)
        {
            url = url.Trim();

            if (url.Length == 0)
                throw new InvalidOperationException("No link was given.");

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That doesn't look like a link - it needs to start with http.");

            string path = PathFor(url);

            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            Directory.CreateDirectory(CacheDirectory);

            byte[] bytes;

            try
            {
                bytes = await App.HttpClient.GetByteArrayAsync(url, token);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not download {url}");
                App.Logger.WriteException(LOG_IDENT, ex);

                throw new InvalidOperationException("The download failed. Check the link and your connection.");
            }

            if (bytes.Length == 0)
                throw new InvalidOperationException("That link gave us an empty file.");

            string temporary = path + ".part";

            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, path, true);

            App.Logger.WriteLine(LOG_IDENT, $"Cached {bytes.Length} bytes for {url}");

            Prune();

            return path;
        }

        public static void Prune()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return;

                var files = new DirectoryInfo(CacheDirectory).GetFiles("*.gif")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Skip(MaxFiles)
                    .ToList();

                foreach (var file in files)
                    file.Delete();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not prune the background cache: {ex.Message}");
            }
        }
    }
}
