namespace Atrium.Core.Models;

public sealed class UserGameData
{
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public StoreKind? PreferredStoreOverride { get; set; }
    public int TrackedMinutes { get; set; }
    public string? ExeOverride { get; set; }
    public string? CoverOverride { get; set; }
    public List<string> Collections { get; set; } = [];
}
