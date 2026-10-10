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

    public bool IsGlobalSyncing => SourcesVM.IsSyncing;
    public bool CanSync => SourcesVM.CanEditFolders;

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
        SourcesVM.SyncCompleted += RefreshLibraryAsync;
        SourcesVM.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SourcesViewModel.IsSyncing) or nameof(SourcesViewModel.IsEditingFolders))
            {
                OnPropertyChanged(nameof(IsGlobalSyncing));
                OnPropertyChanged(nameof(CanSync));
                SyncLibraryCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public async Task InitializeAsync()
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        try
        {
            System.IO.File.AppendAllText(logPath, "InitializeAsync: Database...\n");
            await _database.InitializeAsync();
            System.IO.File.AppendAllText(logPath, "InitializeAsync: SettingsVM...\n");
            await SettingsVM.InitializeAsync();
            System.IO.File.AppendAllText(logPath, "InitializeAsync: SourcesVM...\n");
            await SourcesVM.InitializeAsync();
            System.IO.File.AppendAllText(logPath, "InitializeAsync: LibraryVM...\n");
            await LibraryVM.InitializeAsync();
            System.IO.File.AppendAllText(logPath, "InitializeAsync: GpuOptimizerVM...\n");
            await GpuOptimizerVM.InitializeAsync();
            System.IO.File.AppendAllText(logPath, "InitializeAsync: Complete!\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(logPath, $"EXCEPTION in InitializeAsync:\n{ex}\n");
            LibraryVM.StatusMessage = $"Startup could not complete: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    public Task SyncLibraryAsync() => SourcesVM.SyncAllAsync();

    private async Task RefreshLibraryAsync()
    {
        await LibraryVM.LoadGamesAsync();
        await GpuOptimizerVM.LoadInstalledGamesAsync();
    }
}
