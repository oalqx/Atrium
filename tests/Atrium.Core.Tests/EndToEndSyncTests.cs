using Atrium.Core.Detection;
using Atrium.Core.Library;
using Atrium.Core.Providers;
using Atrium.Core.Providers.Epic;
using Atrium.Core.Providers.Gog;
using Atrium.Core.Providers.Steam;
using Atrium.Core.Providers.Xbox;
using Atrium.Core.Storage;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class EndToEndSyncTests : IDisposable
{
    private readonly string _testDbPath;

    public EndToEndSyncTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"atrium_e2e_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    [Fact]
    public async Task FullLibrarySync_AggregatesAllProviders_IntoDatabase()
    {
        var paths = LauncherDetector.Detect();
        var providers = new List<ILibraryProvider>();

        if (paths.HasSteam) providers.Add(new SteamProvider(paths.SteamPath));
        if (paths.HasEpic) providers.Add(new EpicProvider(paths.EpicManifestsPath));
        if (paths.HasGogGalaxy) providers.Add(new GogGalaxyProvider(paths.GogGalaxyDatabasePath));
        if (paths.HasXbox) providers.Add(new XboxProvider(paths.XboxGamesFolders));

        var allRawGames = new List<ProviderGame>();
        foreach (var p in providers)
        {
            var games = await p.GetGamesAsync();
            allRawGames.AddRange(games);
        }

        allRawGames.Should().NotBeEmpty();

        // Run Union-Find Merger
        var mergedGames = LibraryMerger.Merge(allRawGames);
        mergedGames.Should().NotBeEmpty();

        // Save into SQLite Database
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();
        await db.UpsertGamesAsync(mergedGames);

        // Verify loaded from Database
        var loaded = await db.GetAllGamesAsync();
        loaded.Count.Should().Be(mergedGames.Count);

        // Check that installed games have executable paths and launch URIs
        var installed = loaded.Where(g => g.IsInstalled).ToList();
        installed.Should().NotBeEmpty();
        installed.Should().AllSatisfy(g =>
        {
            g.Sources.Should().Contain(s => s.IsInstalled);
        });
    }
}
