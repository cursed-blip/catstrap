using System.Text.Json.Serialization;

namespace Bloxstrap.Models.Entities
{
    public class AssetProxyConfigFile
    {
        [JsonPropertyName("replacement_rules")]
        public List<AssetProxyConfigEntry> ReplacementRules { get; set; } = new();
    }

    public class AssetProxyConfigEntry
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("expanded")]
        public bool? Expanded { get; set; }

        [JsonPropertyName("children")]
        public List<AssetProxyConfigEntry>? Children { get; set; }

        [JsonPropertyName("replace_ids")]
        public List<long>? ReplaceIds { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("remove")]
        public bool? Remove { get; set; }

        [JsonPropertyName("with_id")]
        public long? WithId { get; set; }

        [JsonPropertyName("cdn_url")]
        public string? CdnUrl { get; set; }

        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }
    }
}
