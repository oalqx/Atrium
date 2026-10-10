using Atrium.Core.Models;
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
}
