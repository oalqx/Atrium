using System.Collections.ObjectModel;
using System.Text.Json;
using Atrium.Core.Detection;
using Atrium.Core.Library;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed record LauncherItem(
    string Name,
    StoreKind Store,
    bool IsDetected,
    string? Path,
    int GameCount,
    string StatusDescription);

public sealed partial class SourcesViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly LibrarySyncService _syncService;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText = "Ready to sync";

    [ObservableProperty]
    private string _lastSyncDurationText = string.Empty;

    public ObservableCollection<LauncherItem> Launchers { get; } = [];
    public ObservableCollection<string> CustomFolders { get; } = [];

    public SourcesViewModel(LibraryDatabase database, LibrarySyncService syncService)
    {
        _database = database;
        _syncService = syncService;
    }

    public async Task InitializeAsync()
    {
        await LoadCustomFoldersAsync();
        await RefreshLaunchersAsync();
    }

    [RelayCommand]
    public async Task RefreshLaunchersAsync()
    {
        var paths = LauncherDetector.Detect();
        var allGames = await _database.GetAllGamesAsync();

        int steamCount = allGames.Count(g => g.Sources.Any(s => s.Store == StoreKind.Steam));
        int epicCount = allGames.Count(g => g.Sources.Any(s => s.Store == StoreKind.Epic));
        int gogCount = allGames.Count(g => g.Sources.Any(s => s.Store == StoreKind.Gog));
        int xboxCount = allGames.Count(g => g.Sources.Any(s => s.Store == StoreKind.Xbox));
        int localCount = allGames.Count(g => g.Sources.Any(s => s.Store == StoreKind.Local));

        Launchers.Clear();

        Launchers.Add(new LauncherItem(
            Name: "Steam",
            Store: StoreKind.Steam,
            IsDetected: paths.HasSteam,
            Path: paths.SteamPath,
            GameCount: steamCount,
            StatusDescription: paths.HasSteam ? $"Detected at {paths.SteamPath}" : "Steam client not detected"));

        Launchers.Add(new LauncherItem(
            Name: "Epic Games Launcher",
            Store: StoreKind.Epic,
            IsDetected: paths.HasEpic,
            Path: paths.EpicManifestsPath,
            GameCount: epicCount,
            StatusDescription: paths.HasEpic ? "Manifests found" : "Epic launcher manifests not found"));

        Launchers.Add(new LauncherItem(
            Name: "GOG Galaxy / GOG",
            Store: StoreKind.Gog,
            IsDetected: paths.HasGogGalaxy || paths.HasGogStandalone,
            Path: paths.GogGalaxyDatabasePath,
            GameCount: gogCount,
            StatusDescription: paths.HasGogGalaxy ? "GOG Galaxy 2.0 database connected" : "Standalone GOG registry detected"));

        Launchers.Add(new LauncherItem(
            Name: "Xbox / Windows Store",
            Store: StoreKind.Xbox,
            IsDetected: paths.HasXbox,
            Path: paths.XboxGamesFolders.FirstOrDefault(),
            GameCount: xboxCount,
            StatusDescription: paths.HasXbox ? $"{paths.XboxGamesFolders.Count} game directories found" : "No XboxGames directories found"));

        Launchers.Add(new LauncherItem(
            Name: "Custom Local Executables",
            Store: StoreKind.Local,
            IsDetected: CustomFolders.Count > 0,
            Path: $"{CustomFolders.Count} configured folders",
            GameCount: localCount,
            StatusDescription: $"{CustomFolders.Count} custom directories configured"));
    }

    [RelayCommand]
    public async Task SyncAllAsync()
    {
        if (IsSyncing) return;

        IsSyncing = true;
        SyncStatusText = "Scanning all installed launchers and game manifests...";

        try
        {
            var result = await _syncService.SyncAsync(CustomFolders);
            SyncStatusText = $"Sync complete! Merged {result.TotalMergedGames} games ({result.TotalInstalledGames} installed).";
            LastSyncDurationText = $"Completed in {result.Duration.TotalSeconds:F1}s";
            await RefreshLaunchersAsync();
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    public async Task AddCustomFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        if (!CustomFolders.Contains(folderPath))
        {
            CustomFolders.Add(folderPath);
            await SaveCustomFoldersAsync();
            await RefreshLaunchersAsync();
        }
    }

    [RelayCommand]
    public async Task RemoveCustomFolderAsync(string folderPath)
    {
        if (CustomFolders.Remove(folderPath))
        {
            await SaveCustomFoldersAsync();
            await RefreshLaunchersAsync();
        }
    }

    private async Task LoadCustomFoldersAsync()
    {
        CustomFolders.Clear();
        var json = await _database.GetSettingAsync("CustomFolders");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    foreach (var f in list) CustomFolders.Add(f);
                }
            }
            catch { }
        }
    }

    private async Task SaveCustomFoldersAsync()
    {
        var json = JsonSerializer.Serialize(CustomFolders.ToList());
        await _database.SetSettingAsync("CustomFolders", json);
    }
}
