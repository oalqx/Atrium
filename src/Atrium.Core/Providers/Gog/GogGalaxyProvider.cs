using System.Text.Json;
using Atrium.Core.Detection;
using Atrium.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Atrium.Core.Providers.Gog;

public sealed class GogGalaxyProvider : ILibraryProvider
{
    private readonly string? _customDbPath;

    public GogGalaxyProvider(string? customDbPath = null)
    {
        _customDbPath = customDbPath;
    }

    public StoreKind Store => StoreKind.Gog;
    public string DisplayName => "GOG Galaxy Database";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var dbPath = _customDbPath ?? LauncherDetector.DetectGogGalaxyDatabasePath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return Task.FromResult(ProviderStatus.NotInstalled);
        }

        return Task.FromResult(ProviderStatus.Available);
    }

    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        var dbPath = _customDbPath ?? LauncherDetector.DetectGogGalaxyDatabasePath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return Array.Empty<ProviderGame>();
        }

        // Create a temporary read-only snapshot to avoid file locks
        var tempFile = Path.Combine(Path.GetTempPath(), $"galaxy_snapshot_{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(dbPath, tempFile, true);

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = tempFile,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct);

            // Verify essential tables exist
            var tableCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('LibraryReleases', 'GamePieces', 'GamePieceTypes')");

            if (tableCount < 3)
            {
                return Array.Empty<ProviderGame>();
            }

            // Query release keys and associated metadata
            var sql = @"
                SELECT 
                    lr.releaseKey,
                    rp.isDlc,
                    gt.minutesInGame,
                    lp.lastPlayedDate,
                    t.value AS TitleJson,
                    st.value AS SortTitleJson,
                    img.value AS ImagesJson,
                    rel.value AS ReleasesJson,
                    meta.value AS MetaJson
                FROM LibraryReleases lr
                LEFT JOIN ReleaseProperties rp ON rp.releaseKey = lr.releaseKey
                LEFT JOIN GameTimes gt ON gt.releaseKey = lr.releaseKey
                LEFT JOIN LastPlayedDates lp ON lp.gameReleaseKey = lr.releaseKey
                LEFT JOIN GamePieces t ON t.releaseKey = lr.releaseKey AND t.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type='title' LIMIT 1)
                LEFT JOIN GamePieces st ON st.releaseKey = lr.releaseKey AND st.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type='sortingTitle' LIMIT 1)
                LEFT JOIN GamePieces img ON img.releaseKey = lr.releaseKey AND img.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type='originalImages' LIMIT 1)
                LEFT JOIN GamePieces rel ON rel.releaseKey = lr.releaseKey AND rel.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type='allGameReleases' LIMIT 1)
                LEFT JOIN GamePieces meta ON meta.releaseKey = lr.releaseKey AND meta.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type='meta' LIMIT 1)
                WHERE COALESCE(rp.isDlc, 0) = 0";

            var rows = await conn.QueryAsync(sql);
            var results = new List<ProviderGame>();

            foreach (var r in rows)
            {
                ct.ThrowIfCancellationRequested();
                string releaseKey = (string)r.releaseKey;
                if (string.IsNullOrEmpty(releaseKey)) continue;

                var (store, storeId) = ParseReleaseKey(releaseKey);
                if (!store.HasValue) continue;

                // Extract title
                var title = ExtractStringProperty(r.TitleJson as string, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;
                title = SanitizeTitle(title);

                var sortTitle = ExtractStringProperty(r.SortTitleJson as string, "title");
                if (string.IsNullOrWhiteSpace(sortTitle)) sortTitle = title;

                // Extract images
                var artwork = ParseArtwork(r.ImagesJson as string);

                // Extract cross-store release keys
                var crossKeys = ParseReleases(r.ReleasesJson as string);

                // Playtime & last played
                int? playtime = r.minutesInGame != null ? (int)(long)r.minutesInGame : null;
                DateTimeOffset? lastPlayed = null;
                string? lpStr = r.lastPlayedDate?.ToString();
                if (!string.IsNullOrEmpty(lpStr) && long.TryParse(lpStr, out var lp) && lp > 0)
                {
                    lastPlayed = DateTimeOffset.FromUnixTimeSeconds(lp);
                }

                results.Add(new ProviderGame
                {
                    Store = store.Value,
                    StoreId = storeId,
                    Title = title,
                    SortTitle = CleanTitleForSorting(sortTitle),
                    IsInstalled = false, // Owned status from Galaxy cloud cache
                    PlaytimeMinutes = playtime,
                    LastPlayed = lastPlayed,
                    Artwork = artwork,
                    CrossStoreReleaseKeys = crossKeys
                });
            }

            return results;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch { }
        }
    }

    private static (StoreKind? Store, string StoreId) ParseReleaseKey(string releaseKey)
    {
        var idx = releaseKey.IndexOf('_');
        if (idx <= 0) return (null, string.Empty);

        var prefix = releaseKey[..idx].ToLowerInvariant();
        var id = releaseKey[(idx + 1)..];

        return prefix switch
        {
            "epic" => (StoreKind.Epic, id),
            "gog" => (StoreKind.Gog, id),
            "xboxone" => (StoreKind.Xbox, id),
            _ => (null, id)
        };
    }

    private static string? ExtractStringProperty(string? json, string propName)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propName, out var elem))
            {
                return elem.GetString();
            }
        }
        catch { }
        return null;
    }

    private static ArtworkSet ParseArtwork(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new ArtworkSet();
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? cover = null;
            string? bg = null;
            string? icon = null;

            if (doc.RootElement.TryGetProperty("verticalCover", out var cov))
                cover = cov.GetString();
            if (doc.RootElement.TryGetProperty("background", out var b))
                bg = b.GetString();
            if (doc.RootElement.TryGetProperty("squareIcon", out var ic))
                icon = ic.GetString();

            return new ArtworkSet
            {
                CoverUrl = cover,
                BackgroundUrl = bg,
                IconUrl = icon
            };
        }
        catch
        {
            return new ArtworkSet();
        }
    }

    private static IReadOnlyList<string> ParseReleases(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("releases", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in arr.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
                return list;
            }
        }
        catch { }
        return Array.Empty<string>();
    }

    private static string SanitizeTitle(string title)
    {
        return title.Replace("\uFFFD", "").Trim();
    }

    private static string CleanTitleForSorting(string title)
    {
        var t = title.Trim();
        if (t.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            return t[4..].Trim();
        }
        return t;
    }
}
