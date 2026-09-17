using System.Text.Json;

namespace Bloxstrap.Integrations
{
    public static class PlaytimeStore
    {
        private const string LOG_IDENT = "PlaytimeStore";

        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

        private static readonly object _lock = new();

        private static Dictionary<long, long> _totals = new();

        private static bool _loaded;
        private static DateTime _lastFlush = DateTime.MinValue;
        private static DateTime _lastKnownWrite = DateTime.MinValue;

        public static string FilePath => Path.Combine(Paths.Base, "Playtime.json");

        public static long Get(long placeId)
        {
            lock (_lock)
            {
                EnsureLoaded();

                return _totals.TryGetValue(placeId, out long seconds) ? seconds : 0;
            }
        }

        public static long Total
        {
            get
            {
                lock (_lock)
                {
                    EnsureLoaded();
                    return _totals.Values.Sum();
                }
            }
        }

        public static void Add(long placeId, long seconds)
        {
            if (placeId <= 0 || seconds <= 0)
                return;

            lock (_lock)
            {
                EnsureLoaded();

                _totals[placeId] = (_totals.TryGetValue(placeId, out long current) ? current : 0) + seconds;

                if (DateTime.Now - _lastFlush >= FlushInterval)
                    FlushLocked();
            }
        }

        public static void Flush()
        {
            lock (_lock)
                FlushLocked();
        }

        public static void ReloadIfChanged()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return;

                    DateTime written = File.GetLastWriteTimeUtc(FilePath);

                    if (written <= _lastKnownWrite)
                        return;

                    _totals = Read();
                    _lastKnownWrite = written;
                    _loaded = true;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to re-read playtime: {ex.Message}");
                }
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            try
            {
                _totals = Read();

                if (File.Exists(FilePath))
                    _lastKnownWrite = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to read playtime: {ex.Message}");
                _totals = new Dictionary<long, long>();
            }
        }

        private static Dictionary<long, long> Read()
        {
            if (!File.Exists(FilePath))
                return new Dictionary<long, long>();

            string json = File.ReadAllText(FilePath);

            if (String.IsNullOrWhiteSpace(json))
                return new Dictionary<long, long>();

            return JsonSerializer.Deserialize<Dictionary<long, long>>(json) ?? new Dictionary<long, long>();
        }

        private static void FlushLocked()
        {
            try
            {
                Directory.CreateDirectory(Paths.Base);

                File.WriteAllText(FilePath, JsonSerializer.Serialize(_totals));

                _lastFlush = DateTime.Now;
                _lastKnownWrite = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to save playtime: {ex.Message}");
            }
        }
    }
}
