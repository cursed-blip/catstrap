using Bloxstrap.Models.APIs.RoValra;

namespace Bloxstrap.Models
{
    public class DatacentersCache
    {
        [JsonPropertyName("datacenters")]
        public List<RoValraDatacenter> Datacenters { get; set; } = new();

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }
}
