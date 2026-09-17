namespace Bloxstrap.Models.Entities
{
    public class AccountProfile
    {
        public long Id { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string Description { get; set; } = "";

        public DateTime Created { get; set; }

        public bool HasVerifiedBadge { get; set; }

        public bool IsBanned { get; set; }

        public int Followers { get; set; }

        public int Following { get; set; }

        public string BustUrl { get; set; } = "";

        public string FullBodyUrl { get; set; } = "";

        public string HeadshotUrl { get; set; } = "";
    }
}
