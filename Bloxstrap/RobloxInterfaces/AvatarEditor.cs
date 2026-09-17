using System.Text;

using Bloxstrap.Models.APIs.Roblox;
using Bloxstrap.Models.RobloxApi;

namespace Bloxstrap.RobloxInterfaces
{
    public static class AvatarEditor
    {
        private const string LOG_IDENT = "AvatarEditor";

        public static readonly (int Id, string Name)[] AccessoryTypes =
        {
            (8, "Hats"),
            (41, "Hair"),
            (42, "Face"),
            (43, "Neck"),
            (44, "Shoulders"),
            (45, "Front"),
            (46, "Back"),
            (47, "Waist")
        };

        private const int PageSize = 100;

        private const int CreationPageSize = 30;

        private const int CreationPages = 4;

        public static async Task<List<CreationItem>> GetCreationsAsync(long userId)
        {
            var creations = new List<CreationItem>();
            string? cursor = null;

            for (int page = 0; page < CreationPages; page++)
            {
                string path = $"v2/search/items/details?CreatorTargetId={userId}&CreatorType=User&Limit={CreationPageSize}";

                if (!String.IsNullOrEmpty(cursor))
                    path += $"&Cursor={Uri.EscapeDataString(cursor)}";

                CreationsResponse? response;

                try
                {
                    response = await Http.GetJson<CreationsResponse>(UrlBuilder.BuildApiUrl("catalog", path));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not read the published items");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    break;
                }

                if (response?.Data is null || response.Data.Count == 0)
                    break;

                creations.AddRange(response.Data.Where(x => x.Id > 0));

                cursor = response.NextPageCursor;

                if (String.IsNullOrEmpty(cursor))
                    break;
            }

            return creations;
        }

        public static async Task<HashSet<long>> GetOwnedAmongAsync(long userId, IEnumerable<CreationItem> creations)
        {
            var owned = new HashSet<long>();

            if (!CookieAccess)
                return owned;

            foreach (int assetType in creations.Select(x => x.AssetType).Distinct())
            {
                List<InventoryItem> items = await GetOwnedAsync(userId, assetType);

                if (items.Count == 0)
                    continue;

                var ids = items.Select(x => x.AssetId).ToHashSet();

                foreach (CreationItem creation in creations.Where(x => x.AssetType == assetType))
                {
                    if (ids.Contains(creation.Id))
                        owned.Add(creation.Id);
                }
            }

            return owned;
        }

        public static string DescribeAssetType(int assetType) => assetType switch
        {
            2 => "T-Shirt",
            8 => "Hat",
            11 => "Shirt",
            12 => "Pants",
            17 => "Head",
            18 => "Face",
            19 => "Gear",
            24 => "Animation",
            27 => "Torso",
            28 => "Right Arm",
            29 => "Left Arm",
            30 => "Right Leg",
            31 => "Left Leg",
            41 => "Hair",
            42 => "Face Accessory",
            43 => "Neck Accessory",
            44 => "Shoulder Accessory",
            45 => "Front Accessory",
            46 => "Back Accessory",
            47 => "Waist Accessory",
            61 => "Emote",
            64 => "Emote",
            65 => "Pose",
            66 => "Package",
            78 => "Mood",
            _ => "Item"
        };

        public static string GetItemUrl(long assetId) => $"https://www.roblox.com/catalog/{assetId}";

        private const int ThumbnailBatch = 50;

        public static bool CookieAccess => App.Settings.Prop.AllowCookieAccess;

        public static async Task<AvatarResponse?> GetAvatarAsync()
        {
            if (!CookieAccess)
                return null;

            try
            {
                return await Http.AuthGetJson<AvatarResponse>(UrlBuilder.BuildApiUrl("avatar", "v1/avatar"));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not read the avatar");
                App.Logger.WriteException(LOG_IDENT, ex);

                return null;
            }
        }

        public static async Task<List<InventoryItem>> GetOwnedAsync(long userId, int assetTypeId)
        {
            var items = new List<InventoryItem>();

            if (!CookieAccess)
                return items;

            try
            {
                InventoryResponse? page = await Http.AuthGetJson<InventoryResponse>(
                    UrlBuilder.BuildApiUrl("inventory", $"v2/users/{userId}/inventory/{assetTypeId}?limit={PageSize}&sortOrder=Desc"));

                if (page?.Data is not null)
                    items.AddRange(page.Data.Where(x => x.AssetId > 0));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the inventory for asset type {assetTypeId}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            return items;
        }

        public static async Task<Dictionary<long, string>> GetThumbnailUrlsAsync(IEnumerable<long> assetIds)
        {
            var urls = new Dictionary<long, string>();
            var ids = assetIds.Distinct().ToList();

            for (int i = 0; i < ids.Count; i += ThumbnailBatch)
            {
                string batch = String.Join(",", ids.Skip(i).Take(ThumbnailBatch));

                try
                {
                    ApiArrayResponse<AssetThumbnail>? response = await Http.GetJson<ApiArrayResponse<AssetThumbnail>>(
                        UrlBuilder.BuildApiUrl("thumbnails", $"v1/assets?assetIds={batch}&size=150x150&format=Png"));

                    foreach (AssetThumbnail thumbnail in response?.Data ?? new List<AssetThumbnail>())
                    {
                        if (thumbnail.State == "Completed" && thumbnail.ImageUrl.Length > 0)
                            urls[thumbnail.TargetId] = thumbnail.ImageUrl;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fetch a batch of asset thumbnails");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            return urls;
        }

        public static async Task<List<Outfit>> GetOutfitsAsync(long userId)
        {
            if (!CookieAccess)
                return new List<Outfit>();

            try
            {
                OutfitsResponse? response = await Http.AuthGetJson<OutfitsResponse>(
                    UrlBuilder.BuildApiUrl("avatar", $"v1/users/{userId}/outfits?itemsPerPage=50&page=1"));

                return response?.Data ?? new List<Outfit>();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not read the saved avatars");
                App.Logger.WriteException(LOG_IDENT, ex);

                return new List<Outfit>();
            }
        }

        public static async Task<Dictionary<long, string>> GetOutfitThumbnailUrlsAsync(IEnumerable<long> outfitIds)
        {
            var urls = new Dictionary<long, string>();
            var ids = outfitIds.Distinct().ToList();

            for (int i = 0; i < ids.Count; i += ThumbnailBatch)
            {
                string batch = String.Join(",", ids.Skip(i).Take(ThumbnailBatch));

                try
                {
                    ApiArrayResponse<AssetThumbnail>? response = await Http.GetJson<ApiArrayResponse<AssetThumbnail>>(
                        UrlBuilder.BuildApiUrl("thumbnails", $"v1/users/outfits?userOutfitIds={batch}&size=150x150&format=Png"));

                    foreach (AssetThumbnail thumbnail in response?.Data ?? new List<AssetThumbnail>())
                    {
                        if (thumbnail.State == "Completed" && thumbnail.ImageUrl.Length > 0)
                            urls[thumbnail.TargetId] = thumbnail.ImageUrl;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fetch a batch of outfit thumbnails");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            return urls;
        }

        public static async Task<(bool Ok, string Message)> WearOutfitAsync(long outfitId)
        {
            if (!CookieAccess)
                return (false, "Catstrap needs permission to use your Roblox login first.");

            OutfitDetails? outfit;

            try
            {
                outfit = await Http.AuthGetJson<OutfitDetails>(UrlBuilder.BuildApiUrl("avatar", $"v1/outfits/{outfitId}/details"));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read outfit {outfitId}");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Couldn't read that saved avatar.");
            }

            if (outfit is null || outfit.Assets.Count == 0)
                return (false, "That saved avatar has nothing in it.");

            (bool ok, string message) = await SetWornAsync(outfit.Assets.Select(x => x.Id).ToList());

            if (!ok)
                return (false, message);

            if (outfit.BodyColors is not null)
            {
                await PostJsonAsync("v1/avatar/set-body-colors", new
                {
                    headColorId = outfit.BodyColors.HeadColorId,
                    torsoColorId = outfit.BodyColors.TorsoColorId,
                    rightArmColorId = outfit.BodyColors.RightArmColorId,
                    leftArmColorId = outfit.BodyColors.LeftArmColorId,
                    rightLegColorId = outfit.BodyColors.RightLegColorId,
                    leftLegColorId = outfit.BodyColors.LeftLegColorId
                });
            }

            if (outfit.Scale is not null)
            {
                await PostJsonAsync("v1/avatar/set-scales", new
                {
                    height = outfit.Scale.Height,
                    width = outfit.Scale.Width,
                    head = outfit.Scale.Head,
                    depth = outfit.Scale.Depth,
                    proportion = outfit.Scale.Proportion,
                    bodyType = outfit.Scale.BodyType
                });
            }

            App.Logger.WriteLine(LOG_IDENT, $"Wearing saved avatar {outfitId} ({outfit.Name})");

            return (true, $"Wearing {outfit.Name}.");
        }

        private static async Task<bool> PostJsonAsync(string path, object payload)
        {
            string body = JsonSerializer.Serialize(payload);

            try
            {
                using HttpResponseMessage response = await App.Cookies.AuthPostWithToken(
                    UrlBuilder.BuildApiUrl("avatar", path),
                    () => new StringContent(body, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                    App.Logger.WriteLine(LOG_IDENT, $"{path} came back {(int)response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{path} failed");
                App.Logger.WriteException(LOG_IDENT, ex);

                return false;
            }
        }

        public static async Task<(bool Ok, string Message)> SetWornAsync(IReadOnlyCollection<long> assetIds)
        {
            if (!CookieAccess)
                return (false, "Catstrap needs permission to use your Roblox login first (see above).");

            if (assetIds.Count == 0)
                return (false, "Catstrap won't send an empty outfit - equip something else first.");

            Uri uri = UrlBuilder.BuildApiUrl("avatar", "v2/avatar/set-wearing-assets");

            Func<HttpContent> body = () => new StringContent(
                JsonSerializer.Serialize(new { assets = assetIds.Select(id => new { id }).ToList() }),
                Encoding.UTF8,
                "application/json");

            try
            {
                using HttpResponseMessage response = await App.Cookies.AuthPostWithToken(uri, body);

                string content = await response.Content.ReadAsStringAsync();

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    return (false, "Roblox turned the request down. Sign in to Roblox again and retry.");

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Avatar change failed with {(int)response.StatusCode}: {content}");

                    return (false, "Roblox wouldn't apply that change.");
                }

                SetWearingResponse? result = JsonSerializer.Deserialize<SetWearingResponse>(content);

                if (result is null)
                    return (false, "Roblox didn't confirm the change.");

                if (!result.Success && result.InvalidAssetIds.Count > 0)
                    return (false, $"{result.InvalidAssetIds.Count} item(s) couldn't be worn - you may not own them any more.");

                return (true, "");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not change the avatar");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Couldn't reach Roblox to change that.");
            }
        }

        public static async Task<(bool Ok, string Message, bool NowWorn)> ToggleAsync(long assetId)
        {
            AvatarResponse? avatar = await GetAvatarAsync();

            if (avatar is null)
                return (false, "Couldn't read what you're wearing right now.", false);

            List<long> ids = avatar.Assets.Select(x => x.Id).ToList();

            bool worn = ids.Remove(assetId);

            if (!worn)
                ids.Add(assetId);

            (bool ok, string message) = await SetWornAsync(ids);

            return (ok, message, !worn);
        }
    }
}
