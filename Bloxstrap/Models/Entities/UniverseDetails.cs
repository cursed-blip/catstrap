using System.Collections.Concurrent;

using Bloxstrap.RobloxInterfaces;

namespace Bloxstrap.Models.Entities
{
    public class UniverseDetails
    {
        private static readonly ConcurrentDictionary<long, UniverseDetails> Cache = new();

        public GameDetailResponse Data { get; set; } = null!;

        public ThumbnailResponse Thumbnail { get; set; } = null!;

        public static UniverseDetails? LoadFromCache(long id) => Cache.TryGetValue(id, out UniverseDetails? details) ? details : null;

        public static Task FetchSingle(long id) => FetchBulk(id.ToString());

        public static async Task FetchBulk(string ids)
        {
            Uri gameDetailsUrl = UrlBuilder.BuildApiUrl("games", $"v1/games?universeIds={ids}");
            Uri thumbnailsUrl = UrlBuilder.BuildApiUrl("thumbnails", $"v1/games/icons?universeIds={ids}&returnPolicy=PlaceHolder&size=128x128&format=Png&isCircular=false");

            ApiArrayResponse<GameDetailResponse> gameDetailResponse;

            if (App.Cookies.Loaded)
                gameDetailResponse = await Http.AuthGetJson<ApiArrayResponse<GameDetailResponse>>(gameDetailsUrl);
            else
                gameDetailResponse = await Http.GetJson<ApiArrayResponse<GameDetailResponse>>(gameDetailsUrl);

            if (!gameDetailResponse.Data.Any())
                throw new InvalidHTTPResponseException("Roblox API for Game Details returned invalid data");

            ApiArrayResponse<ThumbnailResponse> universeThumbnailResponse = await Http.GetJson<ApiArrayResponse<ThumbnailResponse>>(thumbnailsUrl);

            if (!universeThumbnailResponse.Data.Any())
                throw new InvalidHTTPResponseException("Roblox API for Game Thumbnails returned invalid data");

            foreach (string strId in ids.Split(','))
            {
                if (!long.TryParse(strId, out long id))
                    continue;

                GameDetailResponse? details = gameDetailResponse.Data.FirstOrDefault(x => x.Id == id);
                ThumbnailResponse? thumbnail = universeThumbnailResponse.Data.FirstOrDefault(x => x.TargetId == id);

                if (details is null || thumbnail is null)
                    continue;

                Cache[id] = new UniverseDetails
                {
                    Data = details,
                    Thumbnail = thumbnail
                };
            }
        }
    }
}
