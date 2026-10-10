namespace Atrium.Core.Models;

public sealed record ArtworkSet
{
    public string? CoverUrl { get; init; }
    public string? LocalCoverPath { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? LocalBackgroundPath { get; init; }
    public string? IconUrl { get; init; }
    public string? LocalIconPath { get; init; }
    public string? LogoUrl { get; init; }
    public string? LocalLogoPath { get; init; }

    public string? EffectiveCover => !string.IsNullOrEmpty(LocalCoverPath) && System.IO.File.Exists(LocalCoverPath)
        ? LocalCoverPath
        : CoverUrl;

    public string? EffectiveBackground => !string.IsNullOrEmpty(LocalBackgroundPath) && System.IO.File.Exists(LocalBackgroundPath)
        ? LocalBackgroundPath
        : BackgroundUrl;
}
