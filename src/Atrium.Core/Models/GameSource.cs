namespace Atrium.Core.Models;

public sealed record GameSource(
    StoreKind Store,
    string StoreId,
    bool IsInstalled,
    string? InstallPath = null,
    string? ExecutablePath = null,
    string? LaunchUri = null,
    int? PlaytimeMinutes = null,
    DateTimeOffset? LastPlayed = null);
