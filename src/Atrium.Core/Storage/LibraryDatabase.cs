using System.Text.Json;
using Atrium.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Atrium.Core.Storage;

public sealed class LibraryDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public LibraryDatabase(string? customDbPath = null)
    {
        if (!string.IsNullOrEmpty(customDbPath))
        {
            _databasePath = customDbPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "Atrium");
            Directory.CreateDirectory(appDir);
            _databasePath = Path.Combine(appDir, "atrium.db");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS SchemaInfo (
                Version INTEGER PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS Games (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                SortTitle TEXT,
                MetadataJson TEXT,
                ArtworkJson TEXT,
                UpdatedAt INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Sources (
                GameId TEXT NOT NULL,
                Store INTEGER NOT NULL,
                StoreId TEXT NOT NULL,
                IsInstalled INTEGER NOT NULL,
                InstallPath TEXT,
                ExecutablePath TEXT,
                LaunchUri TEXT,
                PlaytimeMinutes INTEGER,
                LastPlayed INTEGER,
                PRIMARY KEY (Store, StoreId),
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Sources_GameId ON Sources(GameId);

            CREATE TABLE IF NOT EXISTS UserData (
                GameId TEXT PRIMARY KEY,
                Favorite INTEGER NOT NULL DEFAULT 0,
                Hidden INTEGER NOT NULL DEFAULT 0,
                PreferredStoreOverride INTEGER,
                TrackedMinutes INTEGER NOT NULL DEFAULT 0,
                ExeOverride TEXT,
                CoverOverride TEXT,
                CollectionsJson TEXT,
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS MergeRules (
                StoreA INTEGER NOT NULL,
                IdA TEXT NOT NULL,
                StoreB INTEGER NOT NULL,
                IdB TEXT NOT NULL,
                Kind TEXT NOT NULL,
                PRIMARY KEY (StoreA, IdA, StoreB, IdB)
            );

            CREATE TABLE IF NOT EXISTS GpuPreferences (
                ExePath TEXT PRIMARY KEY,
                PreviousValue TEXT,
                AppliedAt INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PlaySessions (
                Id TEXT PRIMARY KEY,
                GameId TEXT NOT NULL,
                Store INTEGER NOT NULL,
                StoreId TEXT NOT NULL,
                StartedAt INTEGER NOT NULL,
                EndedAt INTEGER,
                DurationMinutes INTEGER
            );
        ";

        await conn.ExecuteAsync(sql);
    }

    public async Task UpsertGamesAsync(IEnumerable<Game> games)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var g in games)
        {
            var metaJson = g.Metadata != null ? JsonSerializer.Serialize(g.Metadata) : null;
            var artJson = JsonSerializer.Serialize(g.Artwork);

            await conn.ExecuteAsync(@"
                INSERT INTO Games (Id, Title, SortTitle, MetadataJson, ArtworkJson, UpdatedAt)
                VALUES (@Id, @Title, @SortTitle, @MetadataJson, @ArtworkJson, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    SortTitle = excluded.SortTitle,
                    MetadataJson = excluded.MetadataJson,
                    ArtworkJson = excluded.ArtworkJson,
                    UpdatedAt = excluded.UpdatedAt;",
                new
                {
                    Id = g.Id.ToString(),
                    g.Title,
                    g.SortTitle,
                    MetadataJson = metaJson,
                    ArtworkJson = artJson,
                    UpdatedAt = now
                }, tx);

            // Upsert sources
            foreach (var s in g.Sources)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Sources (GameId, Store, StoreId, IsInstalled, InstallPath, ExecutablePath, LaunchUri, PlaytimeMinutes, LastPlayed)
                    VALUES (@GameId, @Store, @StoreId, @IsInstalled, @InstallPath, @ExecutablePath, @LaunchUri, @PlaytimeMinutes, @LastPlayed)
                    ON CONFLICT(Store, StoreId) DO UPDATE SET
                        GameId = excluded.GameId,
                        IsInstalled = excluded.IsInstalled,
                        InstallPath = excluded.InstallPath,
                        ExecutablePath = excluded.ExecutablePath,
                        LaunchUri = excluded.LaunchUri,
                        PlaytimeMinutes = excluded.PlaytimeMinutes,
                        LastPlayed = excluded.LastPlayed;",
                    new
                    {
                        GameId = g.Id.ToString(),
                        Store = (int)s.Store,
                        s.StoreId,
                        IsInstalled = s.IsInstalled ? 1 : 0,
                        s.InstallPath,
                        s.ExecutablePath,
                        s.LaunchUri,
                        s.PlaytimeMinutes,
                        LastPlayed = s.LastPlayed?.ToUnixTimeSeconds()
                    }, tx);
            }

            // Ensure UserData exists
            var collectionsJson = JsonSerializer.Serialize(g.User.Collections);
            await conn.ExecuteAsync(@"
                INSERT INTO UserData (GameId, Favorite, Hidden, PreferredStoreOverride, TrackedMinutes, ExeOverride, CoverOverride, CollectionsJson)
                VALUES (@GameId, @Favorite, @Hidden, @PreferredStoreOverride, @TrackedMinutes, @ExeOverride, @CoverOverride, @CollectionsJson)
                ON CONFLICT(GameId) DO UPDATE SET
                    TrackedMinutes = TrackedMinutes + excluded.TrackedMinutes;",
                new
                {
                    GameId = g.Id.ToString(),
                    Favorite = g.User.Favorite ? 1 : 0,
                    Hidden = g.User.Hidden ? 1 : 0,
                    PreferredStoreOverride = (int?)g.User.PreferredStoreOverride,
                    g.User.TrackedMinutes,
                    g.User.ExeOverride,
                    g.User.CoverOverride,
                    CollectionsJson = collectionsJson
                }, tx);
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<Game>> GetAllGamesAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var gamesDict = new Dictionary<string, Game>();

        var games = await conn.QueryAsync(@"
            SELECT g.Id, g.Title, g.SortTitle, g.MetadataJson, g.ArtworkJson,
                   u.Favorite, u.Hidden, u.PreferredStoreOverride, u.TrackedMinutes, u.ExeOverride, u.CoverOverride, u.CollectionsJson
            FROM Games g
            LEFT JOIN UserData u ON u.GameId = g.Id");

        foreach (var row in games)
        {
            var id = Guid.Parse((string)row.Id);
            var metadata = row.MetadataJson != null ? JsonSerializer.Deserialize<GameMetadata>((string)row.MetadataJson) : null;
            var artwork = row.ArtworkJson != null ? JsonSerializer.Deserialize<ArtworkSet>((string)row.ArtworkJson) ?? new() : new();
            var collections = row.CollectionsJson != null ? JsonSerializer.Deserialize<List<string>>((string)row.CollectionsJson) ?? [] : new List<string>();

            var g = new Game
            {
                Id = id,
                Title = (string)row.Title,
                SortTitle = (string)(row.SortTitle ?? row.Title),
                Metadata = metadata,
                Artwork = artwork,
                User = new UserGameData
                {
                    Favorite = row.Favorite == 1,
                    Hidden = row.Hidden == 1,
                    PreferredStoreOverride = row.PreferredStoreOverride != null ? (StoreKind)(int)row.PreferredStoreOverride : null,
                    TrackedMinutes = (int)(row.TrackedMinutes ?? 0),
                    ExeOverride = (string?)row.ExeOverride,
                    CoverOverride = (string?)row.CoverOverride,
                    Collections = collections
                }
            };

            gamesDict[row.Id] = g;
        }

        var sources = await conn.QueryAsync(@"
            SELECT GameId, Store, StoreId, IsInstalled, InstallPath, ExecutablePath, LaunchUri, PlaytimeMinutes, LastPlayed
            FROM Sources");

        foreach (var s in sources)
        {
            var gameId = (string)s.GameId;
            if (gamesDict.TryGetValue(gameId, out var g))
            {
                DateTimeOffset? lastPlayed = s.LastPlayed != null ? DateTimeOffset.FromUnixTimeSeconds((long)s.LastPlayed) : null;
                g.Sources.Add(new GameSource(
                    Store: (StoreKind)(int)s.Store,
                    StoreId: (string)s.StoreId,
                    IsInstalled: s.IsInstalled == 1,
                    InstallPath: (string?)s.InstallPath,
                    ExecutablePath: (string?)s.ExecutablePath,
                    LaunchUri: (string?)s.LaunchUri,
                    PlaytimeMinutes: s.PlaytimeMinutes != null ? (int?)s.PlaytimeMinutes : null,
                    LastPlayed: lastPlayed));
            }
        }

        return gamesDict.Values.OrderBy(g => g.SortTitle).ToList();
    }

    public async Task SetFavoriteAsync(Guid gameId, bool favorite)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(@"
            UPDATE UserData SET Favorite = @Favorite WHERE GameId = @GameId",
            new { GameId = gameId.ToString(), Favorite = favorite ? 1 : 0 });
    }

    public async Task SetHiddenAsync(Guid gameId, bool hidden)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(@"
            UPDATE UserData SET Hidden = @Hidden WHERE GameId = @GameId",
            new { GameId = gameId.ToString(), Hidden = hidden ? 1 : 0 });
    }

    public async Task SetPreferredStoreAsync(Guid gameId, StoreKind? store)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(@"
            UPDATE UserData SET PreferredStoreOverride = @Store WHERE GameId = @GameId",
            new { GameId = gameId.ToString(), Store = (int?)store });
    }

    public void Dispose()
    {
    }
}
