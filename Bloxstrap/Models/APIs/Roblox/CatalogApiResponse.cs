namespace Bloxstrap.Models.APIs.Roblox
{
    public class CreationItem
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("assetType")]
        public int AssetType { get; set; }

        [JsonPropertyName("price")]
        public int Price { get; set; }
    }

    public class CreationsResponse
    {
        [JsonPropertyName("data")]
        public List<CreationItem> Data { get; set; } = new();

        [JsonPropertyName("nextPageCursor")]
        public string? NextPageCursor { get; set; }
    }
}
