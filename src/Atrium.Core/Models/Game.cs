namespace Atrium.Core.Models;

public sealed class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string SortTitle { get; set; } = string.Empty;
    public List<GameSource> Sources { get; init; } = [];
    public GameMetadata? Metadata { get; set; }
    public ArtworkSet Artwork { get; set; } = new();
    public UserGameData User { get; set; } = new();

    public bool IsInstalled => Sources.Any(s => s.IsInstalled);

    public int TotalPlaytimeMinutes =>
        Sources.Sum(s => s.PlaytimeMinutes ?? 0) + User.TrackedMinutes;

    public DateTimeOffset? MostRecentPlayed =>
        Sources.Select(s => s.LastPlayed).Where(d => d.HasValue).DefaultIfEmpty(null).Max();
}
