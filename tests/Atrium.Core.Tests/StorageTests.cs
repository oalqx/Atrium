using Atrium.Core.Models;
using Atrium.Core.Library;
using Atrium.Core.Providers;
using Atrium.Core.Storage;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class StorageTests : IDisposable
{
    private readonly string _tempDbPath;

    public StorageTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"atrium_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task InitializeAndUpsert_PersistsGamesAndPreservesPreferences()
    {
        using var db = new LibraryDatabase(_tempDbPath);
        await db.InitializeAsync();

        var gameId = Guid.NewGuid();
        var game = new Game
        {
            Id = gameId,
            Title = "Half-Life 2",
            SortTitle = "Half-Life 2",
            User = new UserGameData { Favorite = true }
        };
        game.Sources.Add(new GameSource(
            Store: StoreKind.Steam,
            StoreId: "220",
            IsInstalled: true,
            LaunchUri: "steam://rungameid/220",
            PlaytimeMinutes: 450));

        await db.UpsertGamesAsync(new[] { game });

        var loaded = await db.GetAllGamesAsync();
        loaded.Should().HaveCount(1);
        var loadedGame = loaded[0];
        loadedGame.Title.Should().Be("Half-Life 2");
        loadedGame.User.Favorite.Should().BeTrue();
        loadedGame.Sources.Should().HaveCount(1);
        loadedGame.Sources[0].PlaytimeMinutes.Should().Be(450);

        // Toggle favorite via API
        await db.SetFavoriteAsync(gameId, false);
        var reloaded = await db.GetAllGamesAsync();
        reloaded[0].User.Favorite.Should().BeFalse();
    }

    [Fact]
    public async Task RepeatedSync_PreservesIdentityAndUserData_WhenAnotherStoreIsAdded()
    {
        using var db = new LibraryDatabase(_tempDbPath);
        await db.InitializeAsync();
        var steam = new ProviderGame { Store = StoreKind.Steam, StoreId = "620", Title = "Portal 2", IsInstalled = false };
        var first = LibraryMerger.Merge([steam]);
        first[0].User.TrackedMinutes = 25;
        first[0].User.Collections.Add("Co-op");
        await db.UpsertGamesAsync(first);
        await db.SetFavoriteAsync(first[0].Id, true);
        await db.SetHiddenAsync(first[0].Id, true);
        await db.SetPreferredStoreAsync(first[0].Id, StoreKind.Gog);

        var gog = new ProviderGame { Store = StoreKind.Gog, StoreId = "portal", Title = "Portal 2", IsInstalled = true };
        await db.UpsertGamesAsync(LibraryMerger.Merge([gog, steam]));
        // Saving already-loaded games must not count tracked minutes a second time.
        await db.UpsertGamesAsync(await db.GetAllGamesAsync());

        var loaded = await db.GetAllGamesAsync();
        loaded.Should().ContainSingle();
        loaded[0].Id.Should().Be(first[0].Id);
        loaded[0].Sources.Should().HaveCount(2);
        loaded[0].User.Favorite.Should().BeTrue();
        loaded[0].User.Hidden.Should().BeTrue();
        loaded[0].User.PreferredStoreOverride.Should().Be(StoreKind.Gog);
        loaded[0].User.TrackedMinutes.Should().Be(25);
        loaded[0].User.Collections.Should().Equal("Co-op");
    }

    [Fact]
    public async Task Sync_MergesExistingRecords_WithoutLosingTheirPreferences()
    {
        using var db = new LibraryDatabase(_tempDbPath);
        await db.InitializeAsync();
        var steam = new Game { Title = "Steam title", User = new UserGameData { Favorite = true, TrackedMinutes = 10, Collections = ["Favorites"] } };
        steam.Sources.Add(new GameSource(StoreKind.Steam, "620", true));
        var gog = new Game { Title = "GOG title", User = new UserGameData { PreferredStoreOverride = StoreKind.Gog, TrackedMinutes = 20, Collections = ["Co-op"] } };
        gog.Sources.Add(new GameSource(StoreKind.Gog, "portal", false));
        await db.UpsertGamesAsync([steam, gog]);

        var merged = new Game { Title = "Portal 2" };
        merged.Sources.AddRange(steam.Sources.Concat(gog.Sources));
        await db.UpsertGamesAsync([merged]);

        var loaded = await db.GetAllGamesAsync();
        loaded.Should().ContainSingle();
        loaded[0].Sources.Should().HaveCount(2);
        loaded[0].User.Favorite.Should().BeTrue();
        loaded[0].User.PreferredStoreOverride.Should().Be(StoreKind.Gog);
        loaded[0].User.TrackedMinutes.Should().Be(30);
        loaded[0].User.Collections.Should().BeEquivalentTo(["Favorites", "Co-op"]);
    }

    [Fact]
    public async Task Upsert_RollsBackTheBatch_WhenOneGameCannotBeSaved()
    {
        using var db = new LibraryDatabase(_tempDbPath);
        await db.InitializeAsync();
        Func<Task> save = () => db.UpsertGamesAsync([new Game { Title = "Valid" }, new Game { Title = null! }]);
        await save.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();
        (await db.GetAllGamesAsync()).Should().BeEmpty();
    }
}
