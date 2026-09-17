using Bloxstrap.Models.RobloxApi;

namespace Bloxstrap.RobloxInterfaces
{
    public static class AccountProfiles
    {
        private const string LOG_IDENT = "AccountProfiles";

        private const int LogScanLimit = 12;

        private const int LogHeadChars = 256 * 1024;

        private const int ImageCacheLimit = 24;

        private static readonly Regex UserIdPattern = new(@"userid:(\d{3,})", RegexOptions.Compiled);

        private static readonly object _lock = new();

        private static readonly Dictionary<long, AccountProfile> _profiles = new();

        private static readonly Dictionary<string, byte[]> _images = new();

        public static long? FindSignedInUserId()
        {
            try
            {
                string directory = Paths.RobloxLogs;

                if (!Directory.Exists(directory))
                    return null;

                IEnumerable<FileInfo> logs = new DirectoryInfo(directory)
                    .EnumerateFiles("*.log")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(LogScanLimit)
                    .ToList();

                foreach (FileInfo log in logs)
                {
                    string? id = MostCommonUserId(ReadHead(log.FullName, LogHeadChars));

                    if (id is not null && long.TryParse(id, out long parsed) && parsed > 0)
                        return parsed;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not work out which account is signed in");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            return null;
        }

        public static async Task<long?> ResolveUsernameAsync(string username)
        {
            const string LOG_IDENT = "AccountProfiles::ResolveUsernameAsync";

            username = username.Trim().TrimStart('@');

            if (username.Length < 3)
                return null;

            try
            {
                var payload = new StringContent(
                    JsonSerializer.Serialize(new { usernames = new[] { username }, excludeBannedUsers = false }),
                    Encoding.UTF8,
                    "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, UrlBuilder.BuildApiUrl("users", "v1/usernames/users"))
                {
                    Content = payload
                };

                ApiArrayResponse<UsernameLookupEntry>? response = await Http.SendJson<ApiArrayResponse<UsernameLookupEntry>>(request);
                UsernameLookupEntry? match = response?.Data?.FirstOrDefault();

                return match is not null && match.Id > 0 ? match.Id : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not resolve the username {username}");
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        public static async Task<AccountProfile?> FetchAsync(long id, bool forceRefresh = false)
        {
            const string LOG_IDENT = "AccountProfiles::FetchAsync";

            lock (_lock)
            {
                if (!forceRefresh && _profiles.TryGetValue(id, out AccountProfile? cached))
                    return cached;
            }

            try
            {
                GetUserResponse user = await Http.GetJson<GetUserResponse>(UrlBuilder.BuildApiUrl("users", $"v1/users/{id}"));

                var profile = new AccountProfile
                {
                    Id = user.Id,
                    Username = user.Name,
                    DisplayName = String.IsNullOrWhiteSpace(user.DisplayName) ? user.Name : user.DisplayName,
                    Description = user.Description ?? "",
                    Created = user.Created,
                    HasVerifiedBadge = user.HasVerifiedBadge,
                    IsBanned = user.IsBanned,
                    BustUrl = await GetThumbnailUrlAsync("avatar-bust", id, "420x420"),
                    FullBodyUrl = await GetThumbnailUrlAsync("avatar", id, "720x720"),
                    HeadshotUrl = await GetThumbnailUrlAsync("avatar-headshot", id, "150x150")
                };

                profile.Followers = await GetCountAsync(id, "followers/count");
                profile.Following = await GetCountAsync(id, "followings/count");

                lock (_lock)
                {
                    _profiles[id] = profile;
                }

                return profile;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not fetch the profile for {id}");
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        public static async Task<byte[]?> DownloadImageAsync(string url)
        {
            if (String.IsNullOrEmpty(url))
                return null;

            lock (_lock)
            {
                if (_images.TryGetValue(url, out byte[]? cached))
                    return cached;
            }

            try
            {
                byte[] bytes = await App.HttpClient.GetByteArrayAsync(url);

                lock (_lock)
                {
                    if (_images.Count >= ImageCacheLimit)
                        _images.Clear();

                    _images[url] = bytes;
                }

                return bytes;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not download {url}: {ex.Message}");
                return null;
            }
        }

        public static async Task<byte[]?> DownloadHeadshotAsync(long id)
        {
            string url = await GetThumbnailUrlAsync("avatar-headshot", id, "150x150");

            return url.Length == 0 ? null : await DownloadImageAsync(url);
        }

        private static async Task<string> GetThumbnailUrlAsync(string kind, long id, string size)
        {
            try
            {
                ApiArrayResponse<ThumbnailResponse>? response = await Http.GetJson<ApiArrayResponse<ThumbnailResponse>>(
                    UrlBuilder.BuildApiUrl("thumbnails", $"v1/users/{kind}?userIds={id}&size={size}&format=Png&isCircular=false"));

                return response?.Data?.FirstOrDefault(x => x.State == "Completed")?.ImageUrl ?? "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No {kind} for {id}: {ex.Message}");
                return "";
            }
        }

        private static async Task<int> GetCountAsync(long id, string path)
        {
            try
            {
                FollowCountResponse? response = await Http.GetJson<FollowCountResponse>(
                    UrlBuilder.BuildApiUrl("friends", $"v1/users/{id}/{path}"));

                return response?.Count ?? 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No {path} for {id}: {ex.Message}");
                return 0;
            }
        }

        private static string? MostCommonUserId(string text)
        {
            if (String.IsNullOrEmpty(text))
                return null;

            var counts = new Dictionary<string, int>();

            foreach (Match match in UserIdPattern.Matches(text))
            {
                string id = match.Groups[1].Value;
                counts[id] = (counts.TryGetValue(id, out int count) ? count : 0) + 1;
            }

            return counts.Keys.OrderByDescending(id => counts[id]).FirstOrDefault();
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
    }
}
