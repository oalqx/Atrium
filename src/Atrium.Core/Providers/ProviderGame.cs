using Atrium.Core.Models;

namespace Atrium.Core.Providers;

public sealed record ProviderGame
{
    public required StoreKind Store { get; init; }
    public required string StoreId { get; init; }
    public required string Title { get; init; }
    public string? SortTitle { get; init; }
    public bool IsInstalled { get; init; }
    public string? InstallPath { get; init; }
    public string? ExecutablePath { get; init; }
    public string? LaunchUri { get; init; }
    public int? PlaytimeMinutes { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }
    public ArtworkSet Artwork { get; init; } = new();
    public GameMetadata? Metadata { get; init; }
    public IReadOnlyList<string> CrossStoreReleaseKeys { get; init; } = Array.Empty<string>();
    public bool IsDlc { get; init; }
    public bool IsToolOrRedistributable { get; init; }

    public GameSource ToSource() => new(
        Store: Store,
        StoreId: StoreId,
        IsInstalled: IsInstalled,
        InstallPath: InstallPath,
        ExecutablePath: ExecutablePath,
        LaunchUri: LaunchUri,
        PlaytimeMinutes: PlaytimeMinutes,
        LastPlayed: LastPlayed);
}
