namespace Atrium.Core.Models;

public sealed record GameMetadata
{
    public string? Summary { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Developers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Publishers { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ReleaseDate { get; init; }
    public float? Rating { get; init; }
}
