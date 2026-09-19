using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;

using Bloxstrap.RobloxInterfaces;
using Bloxstrap.Utility;

namespace Bloxstrap.Integrations
{
    public static class Quickplay
    {
        private const string LOG_IDENT = "Quickplay";

        private const int MaxRecentGames = 50;

        private const int MaxTotalGames = 200;

        private const int PlaytimeTickSeconds = 60;

        private const int MaxParallelRefreshes = 5;

        private const int IconDecodeWidth = 256;

        public static event EventHandler? Changed;

        public static ObservableCollection<QuickplayGame> Games => App.Settings.Prop.QuickplayGames;

        public static string ThumbnailDirectory => Path.Combine(Paths.Base, "Quickplay");

        public static string ShortcutIconDirectory => Path.Combine(Paths.Base, "Shortcuts");

        public static QuickplayGame? Find(long placeId) => Games.FirstOrDefault(x => x.PlaceId == placeId);

        public static bool TryParsePlaceId(string? input, out long placeId)
        {
            placeId = 0;

            if (String.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();

            Match match = Regex.Match(input, @"(?:games|places)/(\d+)", RegexOptions.IgnoreCase);

            if (!match.Success)
                match = Regex.Match(input, @"^(\d{3,})$");

            if (!match.Success)
                return false;

            return Int64.TryParse(match.Groups[1].Value, out placeId) && placeId > 0;
        }

        public static async Task<QuickplayGame> AddAsync(string input)
        {
            if (!TryParsePlaceId(input, out long placeId))
                throw new InvalidDataException("That doesn't look like a Roblox game link or place id.");

            QuickplayGame? existing = Find(placeId);

            if (existing is not null)
            {
                await RefreshDetailsAsync(existing);
                return existing;
            }

            if (Games.Count >= MaxTotalGames)
                throw new InvalidOperationException($"Your Quickplay library is full ({MaxTotalGames} games). Remove one first.");

            var game = new QuickplayGame
            {
                PlaceId = placeId,
                LastPlayed = DateTime.Now
            };

            Games.Add(game);

            await RefreshDetailsAsync(game);

            App.Logger.WriteLine(LOG_IDENT, $"Added '{game.DisplayName}' (place {game.PlaceId}, universe {game.UniverseId})");

            RaiseChanged();

            return game;
        }

        public static void Remove(QuickplayGame game)
        {
            if (Games.Remove(game))
                RaiseChanged();
        }

        public static void ToggleFavorite(QuickplayGame game)
        {
            game.IsFavorite = !game.IsFavorite;
            RaiseChanged();
        }

        public static async Task RefreshDetailsAsync(QuickplayGame game)
        {
            try
            {
                if (game.UniverseId <= 0)
                {
                    Uri universeUrl = UrlBuilder.BuildApiUrl("apis", $"universes/v1/places/{game.PlaceId}/universe");
                    game.UniverseId = (await Http.GetJson<UniverseIdResponse>(universeUrl)).UniverseId;
                }

                if (game.UniverseId <= 0)
                    return;

                await UniverseDetails.FetchBulk(game.UniverseId.ToString());

                UniverseDetails? details = UniverseDetails.LoadFromCache(game.UniverseId);

                if (details is null)
                    return;

                game.Name = details.Data.Name;

                if (String.IsNullOrWhiteSpace(game.IconUrl))
                    game.IconUrl = details.Thumbnail.ImageUrl ?? "";

                if (details.Data.Playing > 0)
                    game.PlayingCount = details.Data.Playing;

                await LoadIconAsync(game);

                App.Logger.WriteLine(LOG_IDENT, $"Resolved place {game.PlaceId} to '{game.Name}' (universe {game.UniverseId})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch details for place {game.PlaceId}: {ex.Message}");
            }
        }

        public static async Task RefreshAllAsync()
        {
            List<QuickplayGame> games = Games.ToList();

            using var gate = new SemaphoreSlim(MaxParallelRefreshes);

            await Task.WhenAll(games.Select(game => RunAsync(gate, game)));

            RaiseChanged();
        }

        private static async Task RunAsync(SemaphoreSlim gate, QuickplayGame game)
        {
            await gate.WaitAsync();

            try
            {
                await RefreshDetailsAsync(game);
            }
            finally
            {
                gate.Release();
            }
        }

        public static async Task PrefetchAsync()
        {
            int budget = Math.Max(0, App.Settings.Prop.QuickplayIconPrefetch);

            List<QuickplayGame> pending = Games
                .Where(x => x.Icon is null && x.PlaceId > 0)
                .OrderByDescending(x => x.LastPlayed)
                .Take(budget)
                .ToList();

            PruneThumbnailCache();
            ApplyPlaytime();

            using (var gate = new SemaphoreSlim(MaxParallelRefreshes))
                await Task.WhenAll(pending.Select(game => RunAsync(gate, game)));

            if (App.Settings.Prop.ShowLivePlayerCounts)
                SetLiveCounts(await FetchLiveCountsAsync(Games.Where(x => x.UniverseId > 0).Select(x => x.UniverseId)));

            RefreshShortcutState();

            RaiseChanged();
        }

        public static async Task<Dictionary<long, long>> FetchLiveCountsAsync(IEnumerable<long> universeIds)
        {
            var counts = new Dictionary<long, long>();

            List<long> ids = universeIds.Where(x => x > 0).Distinct().ToList();

            if (!ids.Any())
                return counts;

            try
            {
                foreach (IEnumerable<long> chunk in Chunk(ids, 50))
                {
                    string joined = String.Join(",", chunk);
                    Uri url = UrlBuilder.BuildApiUrl("games", $"v1/games?universeIds={joined}");

                    ApiArrayResponse<GameDetailResponse> response = await Http.GetJson<ApiArrayResponse<GameDetailResponse>>(url);

                    if (response?.Data is null)
                        continue;

                    foreach (GameDetailResponse detail in response.Data)
                        counts[detail.Id] = detail.Playing;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch live player counts: {ex.Message}");
            }

            return counts;
        }

        public static void SetLiveCounts(Dictionary<long, long> counts)
        {
            if (!counts.Any())
                return;

            foreach (QuickplayGame game in Games)
            {
                if (counts.TryGetValue(game.UniverseId, out long playing))
                    game.PlayingCount = playing;
            }
        }

        public static async Task RefreshLiveCountsAsync()
        {
            SetLiveCounts(await FetchLiveCountsAsync(Games.Where(x => x.UniverseId > 0).Select(x => x.UniverseId)));
            RaiseChanged();
        }

        public static void RecordRecent(long placeId, long universeId)
        {
            if (!App.Settings.Prop.TrackRecentlyPlayedGames || placeId <= 0)
                return;

            OnUiThread(() =>
            {
                QuickplayGame? game = Find(placeId);

                if (game is null)
                {
                    if (Games.Count >= MaxTotalGames)
                        return;

                    game = new QuickplayGame { PlaceId = placeId };
                    Games.Add(game);
                }

                if (universeId > 0 && game.UniverseId <= 0)
                    game.UniverseId = universeId;

                game.LastPlayed = DateTime.Now;

                PruneRecents();

                RaiseChanged();
            });
        }

        public static void Launch(QuickplayGame game)
        {
            const string LOG_IDENT = "Quickplay::Launch";

            if (game.PlaceId <= 0)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Joining {game.DisplayName} ({game.PlaceId})");

            LaunchDeeplink(game.LaunchDeeplink);
        }

        public static void LaunchDeeplink(string deeplink)
        {
            const string LOG_IDENT = "Quickplay::LaunchDeeplink";

            string? process = Paths.Process;

            if (String.IsNullOrEmpty(process))
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not determine the Catstrap executable path");
                return;
            }

            Process.Start(new ProcessStartInfo(process, $"-player \"{deeplink}\"")
            {
                UseShellExecute = false
            });
        }

        #region Playtime

        private static readonly object _sessionLock = new();

        private static System.Timers.Timer? _playtimeTimer;
        private static long _sessionPlaceId;
        private static DateTime _sessionTickAt;
        private static bool _sessionActive;

        public static void StartSession(long placeId, long universeId)
        {
            if (placeId <= 0)
                return;

            lock (_sessionLock)
            {
                FlushSessionLocked();

                _sessionActive = true;
                _sessionPlaceId = placeId;
                _sessionTickAt = DateTime.Now;

                _playtimeTimer ??= CreatePlaytimeTimer();
                _playtimeTimer.Enabled = true;
            }
        }

        public static void EndSession()
        {
            lock (_sessionLock)
            {
                FlushSessionLocked();

                _sessionActive = false;

                if (_playtimeTimer is not null)
                    _playtimeTimer.Enabled = false;
            }

            PlaytimeStore.Flush();
        }

        private static System.Timers.Timer CreatePlaytimeTimer()
        {
            var timer = new System.Timers.Timer(PlaytimeTickSeconds * 1000)
            {
                AutoReset = true
            };

            timer.Elapsed += (_, _) =>
            {
                lock (_sessionLock)
                    FlushSessionLocked();
            };

            return timer;
        }

        private static void FlushSessionLocked()
        {
            if (!_sessionActive)
                return;

            double seconds = (DateTime.Now - _sessionTickAt).TotalSeconds;
            _sessionTickAt = DateTime.Now;

            if (seconds < 1 || seconds > 3600)
                return;

            long placeId = _sessionPlaceId;
            long elapsed = (long)Math.Round(seconds);

            AddPlaytime(placeId, elapsed);
        }

        public static void AddPlaytime(long placeId, long seconds)
        {
            if (seconds <= 0)
                return;

            PlaytimeStore.Add(placeId, seconds);

            OnUiThread(() =>
            {
                ApplyPlaytime();
                RaiseChanged();
            });
        }

        public static void ApplyPlaytime()
        {
            PlaytimeStore.ReloadIfChanged();

            long total = 0;

            foreach (QuickplayGame game in Games)
            {
                game.PlaytimeSeconds = PlaytimeStore.Get(game.PlaceId);
                total += game.PlaytimeSeconds;
            }

            foreach (QuickplayGame game in Games)
                game.PlaytimeShare = total <= 0 ? 0 : (game.PlaytimeSeconds * 100.0) / total;
        }

        #endregion Playtime

        #region Desktop shortcuts

        public static string GetShortcutPath(QuickplayGame game)
        {
            string name = SanitizeFileName(String.IsNullOrWhiteSpace(game.Name) ? $"Roblox {game.PlaceId}" : game.Name);

            return Path.Combine(Paths.Desktop, $"{name}.lnk");
        }

        public static bool ShortcutExists(QuickplayGame game)
        {
            try
            {
                return File.Exists(GetShortcutPath(game));
            }
            catch
            {
                return false;
            }
        }

        public static void RefreshShortcutState()
        {
            foreach (QuickplayGame game in Games)
                game.HasShortcut = ShortcutExists(game);
        }

        public static string CreateShortcut(QuickplayGame game)
        {
            const string LOG_IDENT = "Quickplay::CreateShortcut";

            string shortcutPath = GetShortcutPath(game);
            string? iconPath = BuildShortcutIcon(game);

            Shortcut.Create(Paths.Process, $"-player \"{game.LaunchDeeplink}\"", shortcutPath, iconPath);

            if (!File.Exists(shortcutPath))
                throw new IOException("Windows would not let Catstrap create the shortcut.");

            App.Logger.WriteLine(LOG_IDENT, $"Created a desktop shortcut for {game.DisplayName} at {shortcutPath}");

            game.HasShortcut = true;

            return shortcutPath;
        }

        private static string? BuildShortcutIcon(QuickplayGame game)
        {
            try
            {
                string source = GetIconPath(game);

                if (!File.Exists(source))
                    return null;

                Directory.CreateDirectory(ShortcutIconDirectory);

                string destination = Path.Combine(ShortcutIconDirectory, $"{GetIconName(game)}.ico");

                byte[] png = File.ReadAllBytes(source);

                if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50)
                    return null;

                int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];

                using (var stream = File.Create(destination))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)1);

                    writer.Write((byte)(width >= 256 ? 0 : width));
                    writer.Write((byte)(height >= 256 ? 0 : height));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)png.Length);
                    writer.Write((uint)22);
                    writer.Write(png);
                }

                return destination;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not build a shortcut icon: {ex.Message}");
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            string cleaned = String.Join(" ", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

            cleaned = cleaned.TrimEnd('.', ' ');

            if (cleaned.Length > 64)
                cleaned = cleaned[..64].TrimEnd('.', ' ');

            return String.IsNullOrWhiteSpace(cleaned) ? "Roblox game" : cleaned;
        }

        #endregion Desktop shortcuts

        #region Icon cache

        private static string GetIconName(QuickplayGame game) =>
            (game.UniverseId > 0 ? game.UniverseId : game.PlaceId).ToString();

        private static string GetIconPath(QuickplayGame game) =>
            Path.Combine(ThumbnailDirectory, $"{GetIconName(game)}.png");

        public static void PruneThumbnailCache()
        {
            int limitMb = App.Settings.Prop.ThumbnailCacheLimitMb;

            if (limitMb <= 0)
                return;

            try
            {
                if (!Directory.Exists(ThumbnailDirectory))
                    return;

                long limit = (long)limitMb * 1024 * 1024;
                var files = new DirectoryInfo(ThumbnailDirectory).GetFiles("*.png");

                long total = files.Sum(x => x.Length);

                if (total <= limit)
                    return;

                foreach (FileInfo file in files.OrderBy(x => x.LastAccessTimeUtc))
                {
                    if (total <= limit)
                        break;

                    long size = file.Length;

                    try
                    {
                        file.Delete();
                        total -= size;
                    }
                    catch
                    {
                    }
                }

                App.Logger.WriteLine(LOG_IDENT, $"Trimmed the icon cache to {FileSize.ByteSize(total)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to trim the icon cache: {ex.Message}");
            }
        }

        private static async Task LoadIconAsync(QuickplayGame game)
        {
            if (String.IsNullOrWhiteSpace(game.IconUrl))
                return;

            try
            {
                Directory.CreateDirectory(ThumbnailDirectory);

                string path = GetIconPath(game);

                if (!File.Exists(path))
                {
                    byte[] bytes = await App.HttpClient.GetByteArrayAsync(game.IconUrl);
                    await File.WriteAllBytesAsync(path, bytes);
                }

                game.Icon = LoadFrozenBitmap(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to cache the icon for place {game.PlaceId}: {ex.Message}");
            }
        }

        private static BitmapImage LoadFrozenBitmap(string path)
        {
            var bitmap = new BitmapImage();

            using (var stream = File.OpenRead(path))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = IconDecodeWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();

            return bitmap;
        }

        #endregion Icon cache

        private static void PruneRecents()
        {
            if (Games.Count <= MaxRecentGames)
                return;

            List<QuickplayGame> stale = Games
                .Where(x => !x.IsFavorite)
                .OrderByDescending(x => x.LastPlayed)
                .Skip(MaxRecentGames)
                .ToList();

            foreach (QuickplayGame game in stale)
                Games.Remove(game);
        }

        private static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int size)
        {
            var chunk = new List<T>(size);

            foreach (T item in source)
            {
                chunk.Add(item);

                if (chunk.Count == size)
                {
                    yield return chunk;
                    chunk = new List<T>(size);
                }
            }

            if (chunk.Count > 0)
                yield return chunk;
        }

        private static void RaiseChanged() =>
            OnUiThread(() => Changed?.Invoke(null, EventArgs.Empty));

        private static void OnUiThread(Action action)
        {
            System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null)
                return;

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }
    }
}
