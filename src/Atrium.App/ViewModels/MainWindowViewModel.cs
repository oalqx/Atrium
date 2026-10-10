using Atrium.Core.Library;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly LibrarySyncService _syncService;

    public LibraryViewModel LibraryVM { get; }
    public SourcesViewModel SourcesVM { get; }
    public GpuOptimizerViewModel GpuOptimizerVM { get; }
    public SettingsViewModel SettingsVM { get; }

    [ObservableProperty]
    private string _currentSection = "Library";

    [ObservableProperty]
    private bool _isGlobalSyncing;

    public MainWindowViewModel(
        LibraryDatabase database,
        LibrarySyncService syncService,
        LibraryViewModel libraryVM,
        SourcesViewModel sourcesVM,
        GpuOptimizerViewModel gpuOptimizerVM,
        SettingsViewModel settingsVM)
    {
        _database = database;
        _syncService = syncService;
        LibraryVM = libraryVM;
        SourcesVM = sourcesVM;
        GpuOptimizerVM = gpuOptimizerVM;
        SettingsVM = settingsVM;
    }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await SettingsVM.InitializeAsync();
        await SourcesVM.InitializeAsync();
        await GpuOptimizerVM.InitializeAsync();
        await LibraryVM.InitializeAsync();
    }

    [RelayCommand]
    public async Task SyncLibraryAsync()
    {
        if (IsGlobalSyncing) return;

        IsGlobalSyncing = true;
        try
        {
            await SourcesVM.SyncAllAsync();
            await LibraryVM.LoadGamesAsync();
            await GpuOptimizerVM.LoadInstalledGamesAsync();
        }
        finally
        {
            IsGlobalSyncing = false;
        }
    }
}
