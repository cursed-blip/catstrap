namespace Bloxstrap.Models.APIs.Roblox
{
    public class AvatarAssetType
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class AvatarAsset
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("assetType")]
        public AvatarAssetType? AssetType { get; set; }
    }

    public class AvatarResponse
    {
        [JsonPropertyName("assets")]
        public List<AvatarAsset> Assets { get; set; } = new();

        [JsonPropertyName("playerAvatarType")]
        public string PlayerAvatarType { get; set; } = "";
    }

    public class InventoryItem
    {
        [JsonPropertyName("assetId")]
        public long AssetId { get; set; }

        [JsonPropertyName("assetName")]
        public string AssetName { get; set; } = "";
    }

    public class InventoryResponse
    {
        [JsonPropertyName("nextPageCursor")]
        public string? NextPageCursor { get; set; }

        [JsonPropertyName("data")]
        public List<InventoryItem> Data { get; set; } = new();
    }

    public class SetWearingResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("invalidAssetIds")]
        public List<long> InvalidAssetIds { get; set; } = new();
    }

    public class Outfit
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("isEditable")]
        public bool IsEditable { get; set; }
    }

    public class OutfitsResponse
    {
        [JsonPropertyName("data")]
        public List<Outfit> Data { get; set; } = new();

        [JsonPropertyName("nextPageCursor")]
        public string? NextPageCursor { get; set; }
    }

    public class BodyColors
    {
        [JsonPropertyName("headColorId")]
        public int HeadColorId { get; set; }

        [JsonPropertyName("torsoColorId")]
        public int TorsoColorId { get; set; }

        [JsonPropertyName("rightArmColorId")]
        public int RightArmColorId { get; set; }

        [JsonPropertyName("leftArmColorId")]
        public int LeftArmColorId { get; set; }

        [JsonPropertyName("rightLegColorId")]
        public int RightLegColorId { get; set; }

        [JsonPropertyName("leftLegColorId")]
        public int LeftLegColorId { get; set; }
    }

    public class AvatarScales
    {
        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("width")]
        public double Width { get; set; }

        [JsonPropertyName("head")]
        public double Head { get; set; }

        [JsonPropertyName("depth")]
        public double Depth { get; set; }

        [JsonPropertyName("proportion")]
        public double Proportion { get; set; }

        [JsonPropertyName("bodyType")]
        public double BodyType { get; set; }
    }

    public class OutfitDetails
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<AvatarAsset> Assets { get; set; } = new();

        [JsonPropertyName("bodyColors")]
        public BodyColors? BodyColors { get; set; }

        [JsonPropertyName("scale")]
        public AvatarScales? Scale { get; set; }

        [JsonPropertyName("playerAvatarType")]
        public string PlayerAvatarType { get; set; } = "";
    }

    public class AssetThumbnail
    {
        [JsonPropertyName("targetId")]
        public long TargetId { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = "";

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = "";
    }
}
