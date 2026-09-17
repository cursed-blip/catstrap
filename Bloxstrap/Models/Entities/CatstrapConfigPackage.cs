using Bloxstrap.Models.Persistable;

namespace Bloxstrap.Models.Entities
{
    public class CatstrapConfigPackage
    {
        public string Product { get; set; } = App.ProjectName;

        public string Version { get; set; } = App.Version;

        public DateTime Created { get; set; } = DateTime.UtcNow;

        public Settings? Settings { get; set; }

        public Dictionary<string, object>? FastFlags { get; set; }
    }
}
