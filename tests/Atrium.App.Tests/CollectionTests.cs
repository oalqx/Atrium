using Atrium.App.ViewModels;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using Xunit;

namespace Atrium.App.Tests;

public class CollectionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"atrium_collections_{Guid.NewGuid():N}");
    public CollectionTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task Collections_AreSavedWithoutOverwritingOtherCards_AndSurviveSync()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var game = new Game { Title = "Portal", SortTitle = "Portal" };
        game.Sources.Add(new(StoreKind.Steam, "400", true));
        await db.UpsertGamesAsync([game]);
        var first = new GameCardViewModel(game, db) { CollectionDraft = "  Co-op  " };
        var second = new GameCardViewModel((await db.GetAllGamesAsync()).Single(), db) { CollectionDraft = "Backlog" };
        await first.AddCollectionAsync();
        await second.AddCollectionAsync();
        second.Collections.Should().BeEquivalentTo(["Co-op", "Backlog"]);
        second.CollectionDraft.Should().BeEmpty();
        second.CollectionDraft = "CO-OP";
        await second.AddCollectionAsync();
        second.Collections.Should().HaveCount(2);
        await second.ToggleFavoriteAsync();

        var scanned = new Game { Title = "Portal" };
        scanned.Sources.Add(new(StoreKind.Steam, "400", false));
        await db.UpsertGamesAsync([scanned]);
        var saved = (await db.GetAllGamesAsync()).Single();
        saved.User.Collections.Should().BeEquivalentTo(["Co-op", "Backlog"]);
        saved.User.Favorite.Should().BeTrue();
        await first.RemoveCollectionAsync("co-OP");
        first.Collections.Should().Equal("Backlog");
        (await db.GetAllGamesAsync()).Single().User.Favorite.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidOrStaleCollectionEdits_KeepTheDraftAndSavedState()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var game = new Game { Title = "Test", User = new() { Collections = ["Backlog"] } };
        await db.UpsertGamesAsync([game]);
        var card = new GameCardViewModel(game, db) { CollectionDraft = new string('x', 81) };
        await card.AddCollectionAsync();
        card.ActionMessage.Should().Contain("Could not save collection");
        card.CollectionDraft.Should().HaveLength(81);
        card.Collections.Should().Equal("Backlog");
        card.CanEdit.Should().BeTrue();
        Func<Task> invalid = () => db.ChangeCollectionAsync(game.Id, "bad\nname", true);
        await invalid.Should().ThrowAsync<ArgumentException>();
        var stale = new GameCardViewModel(new Game(), db) { CollectionDraft = "Co-op" };
        await stale.AddCollectionAsync();
        stale.ActionMessage.Should().Contain("no longer in the saved library");
        stale.CollectionDraft.Should().Be("Co-op");
        stale.Collections.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectionFilter_CombinesSearchStoresAndVisibility_AndRefreshesAfterEdits()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var portal = new Game { Title = "Portal 2", SortTitle = "Portal 2", User = new() { Collections = ["Co-op"] } };
        portal.Sources.Add(new(StoreKind.Steam, "620", true));
        var hidden = new Game { Title = "Hidden", User = new() { Hidden = true, Collections = ["co-op"] } };
        var other = new Game { Title = "Other", User = new() { Collections = ["Backlog"] } };
        await db.UpsertGamesAsync([portal, hidden, other]);
        var library = new LibraryViewModel(db, new Preferences());
        await library.LoadGamesAsync();
        library.CollectionOptions.Should().HaveCount(3);
        library.SelectedCollection = library.CollectionOptions.Single(option => option.Name == "Co-op" || option.Name == "co-op");
        library.FilteredGames.Should().ContainSingle().Which.Id.Should().Be(portal.Id);
        library.SelectedFilter = "Hidden";
        library.FilteredGames.Should().ContainSingle().Which.Id.Should().Be(hidden.Id);
        library.SelectedFilter = "Steam";
        library.SearchText = "Portal";
        library.FilteredGames.Should().ContainSingle();
        library.SearchText = "absent";
        library.ShowEmptyState.Should().BeTrue();
        library.ResetFilters();
        library.HasActiveFilters.Should().BeFalse();
        var card = library.FilteredGames.Single(card => card.Id == portal.Id);
        card.CollectionDraft = "Weekend";
        await card.AddCollectionAsync();
        library.SelectedCollection = library.CollectionOptions.Single(option => option.Name == "Weekend");
        await library.LoadGamesAsync();
        library.SelectedCollection.Name.Should().Be("Weekend");
        var refreshed = library.FilteredGames.Single();
        await refreshed.RemoveCollectionAsync("Weekend");
        library.SelectedCollection.Name.Should().BeNull();
        library.CollectionOptions.Should().NotContain(option => option.Name == "Weekend");
        library.FilteredGames.Should().HaveCount(2);
    }

    private sealed class Preferences : ObservableObject, ILibraryPreferences
    {
        public StoreKind PreferredDefaultStore => StoreKind.Steam;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, true);
    }
}
