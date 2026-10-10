using Atrium.Core.Detection;
using Atrium.Core.Models;
using Atrium.Core.Providers;
using Atrium.Core.Providers.Epic;
using Atrium.Core.Providers.Gog;
using Atrium.Core.Providers.Local;
using Atrium.Core.Providers.Steam;
using Atrium.Core.Providers.Xbox;
using Atrium.Core.Storage;

namespace Atrium.Core.Library;

public sealed record SyncResult(
    int TotalMergedGames,
    int TotalInstalledGames,
    IReadOnlyDictionary<StoreKind, int> GamesPerStore,
    TimeSpan Duration);

public sealed class LibrarySyncService
{
    private readonly LibraryDatabase _database;

    public LibrarySyncService(LibraryDatabase database)
    {
        _database = database;
    }

    public async Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var paths = LauncherDetector.Detect();
        var providers = new List<ILibraryProvider>();

        if (paths.HasSteam)
        {
            providers.Add(new SteamProvider(paths.SteamPath));
        }

        if (paths.HasEpic)
        {
            providers.Add(new EpicProvider(paths.EpicManifestsPath));
        }

        // Prefer GOG Galaxy database snapshot (imports Epic, GOG, Xbox owned digital licenses)
        if (paths.HasGogGalaxy)
        {
            providers.Add(new GogGalaxyProvider(paths.GogGalaxyDatabasePath));
        }
        else if (paths.HasGogStandalone)
        {
            providers.Add(new GogProvider());
        }

        if (paths.HasXbox)
        {
            providers.Add(new XboxProvider(paths.XboxGamesFolders));
        }

        // Local custom directories
        if (customLocalFolders != null)
        {
            providers.Add(new LocalProvider(customLocalFolders.ToList()));
        }

        var allRawGames = new List<ProviderGame>();
        var gamesPerStore = new Dictionary<StoreKind, int>();

        foreach (var provider in providers)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var games = await provider.GetGamesAsync(cancellationToken);
                allRawGames.AddRange(games);

                foreach (var g in games)
                {
                    gamesPerStore[g.Store] = gamesPerStore.GetValueOrDefault(g.Store, 0) + 1;
                }
            }
            catch
            {
                // Silently isolate individual failing provider to prevent blocking overall sync
            }
        }

        var merged = LibraryMerger.Merge(allRawGames);
        await _database.UpsertGamesAsync(merged);

        var duration = DateTimeOffset.UtcNow - startTime;
        int installedCount = merged.Count(m => m.IsInstalled);

        return new SyncResult(
            TotalMergedGames: merged.Count,
            TotalInstalledGames: installedCount,
            GamesPerStore: gamesPerStore,
            Duration: duration);
    }
}
