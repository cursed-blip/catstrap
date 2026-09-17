using System.Text.Json.Serialization;

namespace Bloxstrap.Integrations.AssetProxy
{
    public class AssetProxyStatus
    {
        public const string Identifier = "catstrap-assetproxy";

        [JsonPropertyName("app")]
        public string App { get; set; } = Identifier;

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("requests")]
        public long Requests { get; set; }

        [JsonPropertyName("cached")]
        public long Cached { get; set; }

        [JsonPropertyName("errors")]
        public long Errors { get; set; }

        [JsonPropertyName("rules")]
        public int Rules { get; set; }

        [JsonPropertyName("clientIdleSeconds")]
        public long ClientIdleSeconds { get; set; }

        [JsonPropertyName("robloxRunning")]
        public bool RobloxRunning { get; set; }

        [JsonPropertyName("started")]
        public string Started { get; set; } = "";
    }
}
