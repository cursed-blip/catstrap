namespace Bloxstrap.Models.APIs.Roblox
{
    public class UsernameLookupEntry
    {
        [JsonPropertyName("requestedUsername")]
        public string RequestedUsername { get; set; } = null!;

        [JsonPropertyName("hasVerifiedBadge")]
        public bool HasVerifiedBadge { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = null!;
    }
}
