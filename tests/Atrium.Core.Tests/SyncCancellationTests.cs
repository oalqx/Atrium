using Atrium.Core.Library;
using Atrium.Core.Models;
using Atrium.Core.Providers;
using Atrium.Core.Providers.Epic;
using Atrium.Core.Providers.Gog;
using Atrium.Core.Providers.Local;
using Atrium.Core.Providers.Steam;
using Atrium.Core.Providers.Xbox;
using Atrium.Core.Storage;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public sealed class SyncCancellationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"atrium_sync_{Guid.NewGuid():N}");
    public SyncCancellationTests() => Directory.CreateDirectory(_directory);
    private LibraryDatabase CreateDatabase() => new(Path.Combine(_directory, "library.db"));

    [Fact]
    public async Task ProviderFailure_IsReported_AndPreviouslySavedGamesAreKept()
    {
        using var db = CreateDatabase();
        await db.InitializeAsync();
        var existing = new Game { Title = "Existing", User = new UserGameData { Favorite = true } };
        existing.Sources.Add(new GameSource(StoreKind.Gog, "old", true));
        await db.UpsertGamesAsync([existing]);
        var broken = new TestProvider("Broken store", _ => throw new IOException("Manifest directory is unavailable"));
        var working = new TestProvider("Working store", _ => Task.FromResult<IReadOnlyList<ProviderGame>>([
            new() { Store = StoreKind.Steam, StoreId = "new", Title = "New", IsInstalled = true }
        ]));
        var reports = new List<SyncProgress>();
        var service = new LibrarySyncService(db, _ => [broken, working]);

        var result = await service.SyncAsync(progress: new ImmediateProgress(reports.Add));

        result.ProviderErrors["Broken store"].Should().Contain("unavailable");
        result.TotalMergedGames.Should().Be(1);
        var saved = await db.GetAllGamesAsync();
        saved.Should().HaveCount(2);
        saved.Single(g => g.Id == existing.Id).User.Favorite.Should().BeTrue();
        reports.Last().Stage.Should().Be(SyncStage.Completed);
        reports.Last().Percent.Should().Be(100);
        reports.Select(r => r.Percent).Should().BeInAscendingOrder();
        reports.Should().Contain(r => r.Stage == SyncStage.Scanning && r.Message.Contains("Working store"));
    }

    [Fact]
    public async Task ProviderReturningAfterCancellation_DoesNotSaveItsPartialScan()
    {
        using var db = CreateDatabase();
        await db.InitializeAsync();
        var existing = new Game { Title = "Saved", User = new UserGameData { Favorite = true } };
        await db.UpsertGamesAsync([existing]);
        using var cancellation = new CancellationTokenSource();
        var provider = new TestProvider("Cancelling store", _ =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<ProviderGame>>([
                new() { Store = StoreKind.Local, StoreId = "partial", Title = "Partial", IsInstalled = true }
            ]);
        });
        var service = new LibrarySyncService(db, _ => [provider]);
        Func<Task> action = () => service.SyncAsync(cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();

        var saved = (await db.GetAllGamesAsync()).Single();
        saved.Id.Should().Be(existing.Id);
        saved.User.Favorite.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationAtSaveBoundary_LeavesDatabaseUnchanged()
    {
        using var db = CreateDatabase();
        await db.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var service = new LibrarySyncService(db, _ => []);
        Func<Task> action = () => service.SyncAsync(cancellationToken: cancellation.Token,
            progress: new ImmediateProgress(p => { if (p.Stage == SyncStage.Saving) cancellation.Cancel(); }));
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await db.GetAllGamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationDuringDatabaseWrite_RollsBackEarlierRows()
    {
        using var db = CreateDatabase();
        await db.InitializeAsync();
        var original = new Game { Title = "Original", User = new UserGameData { Hidden = true } };
        await db.UpsertGamesAsync([original]);
        using var cancellation = new CancellationTokenSource();
        IEnumerable<Game> Batch()
        {
            yield return new Game { Title = "First inserted row" };
            cancellation.Cancel();
            yield return new Game { Title = "Second inserted row" };
        }
        Func<Task> action = () => db.UpsertGamesAsync(Batch(), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        var saved = (await db.GetAllGamesAsync()).Single();
        saved.Id.Should().Be(original.Id);
        saved.User.Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task CancelledProviders_ThrowEvenWhenTheirDirectoriesAreMissing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ILibraryProvider[] providers = [new SteamProvider(_directory), new EpicProvider(_directory),
            new GogGalaxyProvider(Path.Combine(_directory, "missing.db")), new GogProvider(),
            new LocalProvider([]), new XboxProvider([])];
        foreach (var provider in providers)
        {
            Func<Task> action = async () => await provider.GetGamesAsync(cancellation.Token);
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Fact]
    public async Task OverlappingLocalFolders_ReturnEachExecutableOnlyOnce()
    {
        var child = Directory.CreateDirectory(Path.Combine(_directory, "nested")).FullName;
        var mainExe = Path.Combine(_directory, "game.exe");
        File.WriteAllText(mainExe, "dummy");
        File.WriteAllText(Path.Combine(child, "second.EXE"), "dummy");
        File.WriteAllText(Path.Combine(child, "setup.exe"), "dummy");
        var games = await new LocalProvider([_directory, child, mainExe]).GetGamesAsync();
        games.Should().HaveCount(2);
        games.Select(g => g.StoreId).Should().OnlyHaveUniqueItems();
    }

    private sealed class ImmediateProgress(Action<SyncProgress> report) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => report(value);
    }

    private sealed class TestProvider(string name, Func<CancellationToken, Task<IReadOnlyList<ProviderGame>>> scan) : ILibraryProvider
    {
        public StoreKind Store => StoreKind.Local;
        public string DisplayName => name;
        public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(ProviderStatus.Available);
        public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default) => scan(ct);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }
}
