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
    string StatusDescription)
{
    public string DetectionLabel => IsDetected ? "Detected" : "Not detected";
}

public sealed record SyncIssue(string Provider, string Message);

public sealed partial class SourcesViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly ILibrarySyncService _syncService;
    private CancellationTokenSource? _syncCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditFolders))]
    [NotifyPropertyChangedFor(nameof(CanCancelSync))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomFolderCommand))]
    private bool _isSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelSync))]
    [NotifyCanExecuteChangedFor(nameof(CancelSyncCommand))]
    private bool _isCancelling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditFolders))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomFolderCommand))]
    private bool _isEditingFolders;

    [ObservableProperty]
    private double _syncProgressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _folderStatusText = string.Empty;

    [ObservableProperty]
    private string _syncStatusText = "Ready to sync";

    [ObservableProperty]
    private string _lastSyncDurationText = string.Empty;

    public ObservableCollection<LauncherItem> Launchers { get; } = [];
    public ObservableCollection<string> CustomFolders { get; } = [];
    public ObservableCollection<SyncIssue> SyncIssues { get; } = [];
    public bool HasSyncIssues => SyncIssues.Count > 0;
    public bool CanEditFolders => !IsSyncing && !IsEditingFolders;
    public bool CanCancelSync => IsSyncing && !IsCancelling && _syncCancellation != null;
    public event Func<Task>? SyncCompleted;

    public SourcesViewModel(LibraryDatabase database, ILibrarySyncService syncService)
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
            StatusDescription: paths.HasGogGalaxy ? "GOG Galaxy 2.0 database connected" :
                paths.HasGogStandalone ? "Standalone GOG registry detected" : "GOG installation not detected"));

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

    [RelayCommand(CanExecute = nameof(CanEditFolders))]
    public async Task SyncAllAsync()
    {
        if (!CanEditFolders) return;

        using var cancellation = new CancellationTokenSource();
        _syncCancellation = cancellation;
        IsCancelling = false;
        IsSyncing = true;
        SyncProgressPercent = 0;
        IsProgressIndeterminate = true;
        LastSyncDurationText = string.Empty;
        SyncIssues.Clear();
        OnPropertyChanged(nameof(HasSyncIssues));
        SyncStatusText = "Scanning all installed launchers and game manifests...";

        try
        {
            var folders = CustomFolders.ToList();
            var progress = new Progress<SyncProgress>(update =>
            {
                if (!ReferenceEquals(_syncCancellation, cancellation) || !IsSyncing) return;
                SyncProgressPercent = update.Percent;
                IsProgressIndeterminate = update.TotalSteps == 0;
                if (!IsCancelling) SyncStatusText = update.Message;
            });
            var result = await Task.Run(() => _syncService.SyncAsync(folders, cancellation.Token, progress));
            // The transaction has committed; late progress/cancel requests must not
            // overwrite the completed result while the views refresh.
            _syncCancellation = null;
            OnPropertyChanged(nameof(CanCancelSync));
            CancelSyncCommand.NotifyCanExecuteChanged();
            SyncProgressPercent = 100;
            IsProgressIndeterminate = false;
            SyncStatusText = $"Sync complete! Merged {result.TotalMergedGames} games ({result.TotalInstalledGames} installed).";
            if (result.ProviderErrors.Count > 0)
            {
                SyncStatusText += $" Could not scan: {string.Join(", ", result.ProviderErrors.Keys)}. Existing entries were kept.";
                foreach (var issue in result.ProviderErrors) SyncIssues.Add(new(issue.Key, issue.Value));
                OnPropertyChanged(nameof(HasSyncIssues));
            }
            LastSyncDurationText = $"Completed in {result.Duration.TotalSeconds:F1}s";
            try
            {
                await RefreshLaunchersAsync();
                if (SyncCompleted != null)
                    foreach (Func<Task> refresh in SyncCompleted.GetInvocationList()) await refresh();
            }
            catch (Exception ex)
            {
                SyncStatusText += $" Library saved, but a view could not refresh: {ex.Message}";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SyncStatusText = "Sync cancelled. Your saved library was kept unchanged.";
            LastSyncDurationText = string.Empty;
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Sync error: {ex.Message}";
        }
        finally
        {
            _syncCancellation = null;
            IsSyncing = false;
            IsCancelling = false;
            IsProgressIndeterminate = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSync))]
    public void CancelSync()
    {
        if (!CanCancelSync || _syncCancellation == null) return;
        IsCancelling = true;
        SyncStatusText = "Cancel requested. Waiting for the current step to finish...";
        _syncCancellation.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanEditFolders))]
    public async Task<bool> AddCustomFolderAsync(string folderPath)
    {
        if (!CanEditFolders) return false;
        FolderStatusText = string.Empty;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            FolderStatusText = "Enter a game folder path.";
            return false;
        }
        IsEditingFolders = true;
        try
        {
            var path = NormalizeFolder(folderPath);
            if (!Directory.Exists(path))
            {
                FolderStatusText = "That folder could not be found. Check the path and try again.";
                return false;
            }
            if (CustomFolders.Any(existing => string.Equals(NormalizeFolder(existing), path, StringComparison.OrdinalIgnoreCase)))
            {
                FolderStatusText = "That folder is already configured.";
                return false;
            }
            await SaveCustomFoldersAsync(CustomFolders.Append(path));
            CustomFolders.Add(path);
            FolderStatusText = "Folder added. Sync the library to find its games.";
        }
        catch (Exception ex) { FolderStatusText = $"Could not add folder: {ex.Message}"; return false; }
        finally { IsEditingFolders = false; }
        try { await RefreshLaunchersAsync(); }
        catch (Exception ex) { FolderStatusText += $" Launcher counts could not be refreshed: {ex.Message}"; }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanEditFolders))]
    public async Task RemoveCustomFolderAsync(string folderPath)
    {
        if (!CanEditFolders || !CustomFolders.Contains(folderPath)) return;
        IsEditingFolders = true;
        try
        {
            await SaveCustomFoldersAsync(CustomFolders.Where(path => path != folderPath));
            CustomFolders.Remove(folderPath);
            FolderStatusText = "Folder removed. Games already in your library are kept.";
        }
        catch (Exception ex) { FolderStatusText = $"Could not remove folder: {ex.Message}"; return; }
        finally { IsEditingFolders = false; }
        try { await RefreshLaunchersAsync(); }
        catch (Exception ex) { FolderStatusText += $" Launcher counts could not be refreshed: {ex.Message}"; }
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

    private static string NormalizeFolder(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));

    private async Task SaveCustomFoldersAsync(IEnumerable<string> folders)
    {
        var json = JsonSerializer.Serialize(folders.ToList());
        await _database.SetSettingAsync("CustomFolders", json);
    }
}
