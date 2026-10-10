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
    TimeSpan Duration)
{
    public IReadOnlyDictionary<string, string> ProviderErrors { get; init; } = new Dictionary<string, string>();
}

public enum SyncStage { Discovering, Scanning, Merging, Saving, Completed }

public sealed record SyncProgress(SyncStage Stage, string Message, int CompletedSteps, int TotalSteps)
{
    public double Percent => TotalSteps > 0 ? Math.Clamp(100.0 * CompletedSteps / TotalSteps, 0, 100) : 0;
}

public interface ILibrarySyncService
{
    Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null,
        CancellationToken cancellationToken = default, IProgress<SyncProgress>? progress = null);
}

public sealed class LibrarySyncService : ILibrarySyncService
{
    private readonly LibraryDatabase _database;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<ILibraryProvider>> _providerFactory;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public LibrarySyncService(LibraryDatabase database,
        Func<IReadOnlyList<string>, IReadOnlyList<ILibraryProvider>>? providerFactory = null)
    {
        _database = database;
        _providerFactory = providerFactory ?? CreateProviders;
    }

    private static IReadOnlyList<ILibraryProvider> CreateProviders(IReadOnlyList<string> customLocalFolders)
    {
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
        if (customLocalFolders.Count > 0)
        {
            providers.Add(new LocalProvider(customLocalFolders));
        }
        return providers;
    }

    public async Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null,
        CancellationToken cancellationToken = default, IProgress<SyncProgress>? progress = null)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var stopwatch = global::System.Diagnostics.Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SyncStage.Discovering, "Detecting launchers...", 0, 0));
            var providers = _providerFactory(customLocalFolders?.ToList() ?? []);
            cancellationToken.ThrowIfCancellationRequested();
            int totalSteps = providers.Count + 2, completed = 0;
            var allRawGames = new List<ProviderGame>();
            var gamesPerStore = new Dictionary<StoreKind, int>();
            var providerErrors = new Dictionary<string, string>();

            foreach (var provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(SyncStage.Scanning, $"Scanning {provider.DisplayName}...", completed, totalSteps));
                try
                {
                    var games = await provider.GetGamesAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    allRawGames.AddRange(games);

                    foreach (var g in games)
                    {
                        gamesPerStore[g.Store] = gamesPerStore.GetValueOrDefault(g.Store, 0) + 1;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    providerErrors[provider.DisplayName] = ex.Message;
                }
                completed++;
                progress?.Report(new(SyncStage.Scanning, $"Finished {provider.DisplayName}.", completed, totalSteps));
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SyncStage.Merging, "Combining games across launchers...", completed, totalSteps));
            var merged = LibraryMerger.Merge(allRawGames, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SyncStage.Saving, "Saving your library...", ++completed, totalSteps));
            await _database.UpsertGamesAsync(merged, cancellationToken);

            var duration = stopwatch.Elapsed;
            int installedCount = merged.Count(m => m.IsInstalled);

            progress?.Report(new(SyncStage.Completed, "Library saved.", totalSteps, totalSteps));
            return new SyncResult(
                TotalMergedGames: merged.Count,
                TotalInstalledGames: installedCount,
                GamesPerStore: gamesPerStore,
                Duration: duration) { ProviderErrors = providerErrors };
        }
        finally { _syncLock.Release(); }
    }
}
