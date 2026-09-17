namespace Bloxstrap.Models.Entities
{
    public class PinnedClientVersion
    {
        public string VersionGuid { get; set; } = "";

        public string Version { get; set; } = "";

        public DateTime? DeployedAt { get; set; }

        public DateTime PinnedAt { get; set; } = DateTime.Now;

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                string label = String.IsNullOrEmpty(Version) ? VersionGuid : $"{Version} ({VersionGuid})";

                if (DeployedAt is not null)
                    label += $" • {DeployedAt.Value.ToLocalTime():yyyy-MM-dd}";

                return label;
            }
        }
    }
}
