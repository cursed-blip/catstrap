namespace Bloxstrap.Models.Entities
{
    public enum AssetProxyAction
    {
        [EnumName(StaticName = "Replace")]
        Replace,

        [EnumName(StaticName = "Remove")]
        Remove,

        [EnumName(StaticName = "Redirect")]
        Redirect
    }

    public class AssetProxyRule
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "";
        public AssetProxyAction Action { get; set; } = AssetProxyAction.Replace;
        public long FromAssetId { get; set; } = 0;

        public long ToAssetId { get; set; } = 0;

        public string RedirectTarget { get; set; } = "";

        public string DisplayName
        {
            get
            {
                if (!String.IsNullOrEmpty(Name))
                    return Name;

                return Action switch
                {
                    AssetProxyAction.Replace => $"Replace {FromAssetId} -> {ToAssetId}",
                    AssetProxyAction.Remove => $"Remove {FromAssetId}",
                    AssetProxyAction.Redirect => $"Redirect {FromAssetId}",
                    _ => $"Rule {FromAssetId}"
                };
            }
        }
    }
}