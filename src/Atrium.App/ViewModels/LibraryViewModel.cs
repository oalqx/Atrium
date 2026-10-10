using System.Collections.ObjectModel;
using System.ComponentModel;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly SettingsViewModel _settings;
    private List<GameCardViewModel> _allGames = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _selectedSort = "Title (A-Z)";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _totalGamesCount;

    [ObservableProperty]
    private int _installedGamesCount;

    [ObservableProperty]
    private GameCardViewModel? _selectedGame;

    [ObservableProperty]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private bool _hasLoadError;

    public ObservableCollection<GameCardViewModel> FilteredGames { get; } = [];
    public bool ShowEmptyState => !IsLoading && FilteredGames.Count == 0;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) || SelectedFilter != "All";
    public string ResultsSummary => $"{FilteredGames.Count} shown · {_allGames.Count(g => !g.IsHidden)} in library · {_allGames.Count(g => g.IsHidden)} hidden";
    public string EmptyTitle => HasLoadError ? "Library could not be loaded" :
        _allGames.Count == 0 ? "Your library starts here" : SelectedFilter == "Hidden" ? "No hidden games" : "No games match";
    public string EmptyDescription => HasLoadError ? "Use Reload to try again. Your saved library is kept on this device." :
        _allGames.Count == 0 ? "Open Launchers & Sources, then sync to find your games." :
        SelectedFilter == "Hidden" ? "Games you hide will appear here. Open their details to restore them." :
        "Try a different search or clear the filters. Hidden games are available in the Hidden filter.";

    public IReadOnlyList<string> FilterOptions { get; } =
    [
        "All",
        "Installed",
        "Favorites",
        "Steam",
        "Epic Games",
        "GOG",
        "Xbox",
        "Local PC",
        "Hidden"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "Title (A-Z)",
        "Title (Z-A)",
        "Playtime (High to Low)",
        "Installed First",
        "Recently Played"
    ];

    public LibraryViewModel(LibraryDatabase database, SettingsViewModel settings)
    {
        _database = database;
        _settings = settings;
        _settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.PreferredDefaultStore))
                foreach (var game in _allGames)
                    game.UpdateStoreOrder(PreferredSourceResolver.CreateStoreOrder(_settings.PreferredDefaultStore));
        };
    }

    public async Task InitializeAsync()
    {
        await LoadGamesAsync();
    }

    [RelayCommand]
    public async Task LoadGamesAsync()
    {
        IsLoading = true;
        HasLoadError = false;
        NotifyResultsChanged();
        StatusMessage = "Loading library...";

        try
        {
            var games = await _database.GetAllGamesAsync();
            CloseDetails();
            foreach (var card in _allGames) card.PropertyChanged -= OnGamePropertyChanged;
            var storeOrder = PreferredSourceResolver.CreateStoreOrder(_settings.PreferredDefaultStore);
            _allGames = games.Select(g => new GameCardViewModel(g, _database, storeOrder)).ToList();
            foreach (var card in _allGames) card.PropertyChanged += OnGamePropertyChanged;

            TotalGamesCount = _allGames.Count;
            InstalledGamesCount = _allGames.Count(g => g.IsInstalled);

            ApplyFilterAndSort();
            StatusMessage = $"{TotalGamesCount} games ({InstalledGamesCount} installed)";
        }
        catch (Exception ex)
        {
            HasLoadError = true;
            StatusMessage = $"Failed to load library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            NotifyResultsChanged();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnSelectedFilterChanged(string value) => ApplyFilterAndSort();
    partial void OnSelectedSortChanged(string value) => ApplyFilterAndSort();

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(GameCardViewModel.IsFavorite) or nameof(GameCardViewModel.IsHidden))
            ApplyFilterAndSort();
        if (args.PropertyName == nameof(GameCardViewModel.ActionMessage) && sender is GameCardViewModel card && card.HasActionMessage)
            StatusMessage = card.ActionMessage;
    }

    [RelayCommand]
    public void SelectGame(GameCardViewModel? game)
    {
        if (game != null) game.ActionMessage = string.Empty;
        if (game != null) game.PendingStorePreference = game.SelectedStorePreference;
        SelectedGame = game;
        IsDetailsOpen = game != null;
    }

    [RelayCommand]
    public void CloseDetails()
    {
        IsDetailsOpen = false;
        SelectedGame = null;
    }

    [RelayCommand]
    public void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedFilter = "All";
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ResultsSummary));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private void ApplyFilterAndSort()
    {
        var query = _allGames.Where(g => SelectedFilter == "Hidden" ? g.IsHidden : !g.IsHidden);

        // 1. Text filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(g => g.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Category / Store filter
        query = SelectedFilter switch
        {
            "Installed" => query.Where(g => g.IsInstalled),
            "Favorites" => query.Where(g => g.IsFavorite),
            "Steam" => query.Where(g => g.AvailableStores.Contains(StoreKind.Steam)),
            "Epic Games" => query.Where(g => g.AvailableStores.Contains(StoreKind.Epic)),
            "GOG" => query.Where(g => g.AvailableStores.Contains(StoreKind.Gog)),
            "Xbox" => query.Where(g => g.AvailableStores.Contains(StoreKind.Xbox)),
            "Local PC" => query.Where(g => g.AvailableStores.Contains(StoreKind.Local)),
            _ => query
        };

        // 3. Sorting
        query = SelectedSort switch
        {
            "Title (A-Z)" => query.OrderBy(g => g.Game.SortTitle),
            "Title (Z-A)" => query.OrderByDescending(g => g.Game.SortTitle),
            "Playtime (High to Low)" => query.OrderByDescending(g => g.PlaytimeMinutes),
            "Installed First" => query.OrderByDescending(g => g.IsInstalled).ThenBy(g => g.Game.SortTitle),
            "Recently Played" => query.OrderByDescending(g => g.Game.MostRecentPlayed).ThenBy(g => g.Game.SortTitle),
            _ => query.OrderBy(g => g.Game.SortTitle)
        };

        var list = query.ToList();
        FilteredGames.Clear();
        foreach (var item in list)
        {
            FilteredGames.Add(item);
        }
        NotifyResultsChanged();
    }
}
