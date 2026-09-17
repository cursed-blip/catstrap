namespace Bloxstrap.Models.Entities
{
    public class ClientVersionEntry
    {
        public string VersionGuid { get; set; } = "";

        public string Version { get; set; } = "";

        public DateTime? DeployedAt { get; set; }

        public string Source { get; set; } = "";

        public bool IsLocal { get; set; }

        public bool IsInstalled { get; set; }

        public string DisplayText => VersionGuid;
    }
}
