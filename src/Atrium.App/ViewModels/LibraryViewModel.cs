using System.Collections.ObjectModel;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
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

    public ObservableCollection<GameCardViewModel> FilteredGames { get; } = [];

    public IReadOnlyList<string> FilterOptions { get; } =
    [
        "All",
        "Installed",
        "Favorites",
        "Steam",
        "Epic Games",
        "GOG",
        "Xbox",
        "Local PC"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "Title (A-Z)",
        "Title (Z-A)",
        "Playtime (High to Low)",
        "Installed First"
    ];

    public LibraryViewModel(LibraryDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        await LoadGamesAsync();
    }

    [RelayCommand]
    public async Task LoadGamesAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading library...";

        try
        {
            var games = await _database.GetAllGamesAsync();
            _allGames = games.Select(g => new GameCardViewModel(g, _database)).ToList();

            TotalGamesCount = _allGames.Count;
            InstalledGamesCount = _allGames.Count(g => g.IsInstalled);

            ApplyFilterAndSort();
            StatusMessage = $"{TotalGamesCount} games ({InstalledGamesCount} installed)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnSelectedFilterChanged(string value) => ApplyFilterAndSort();
    partial void OnSelectedSortChanged(string value) => ApplyFilterAndSort();

    [RelayCommand]
    public void SelectGame(GameCardViewModel? game)
    {
        SelectedGame = game;
        IsDetailsOpen = game != null;
    }

    [RelayCommand]
    public void CloseDetails()
    {
        IsDetailsOpen = false;
        SelectedGame = null;
    }

    private void ApplyFilterAndSort()
    {
        var query = _allGames.AsEnumerable();

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
            _ => query.OrderBy(g => g.Game.SortTitle)
        };

        var list = query.ToList();
        FilteredGames.Clear();
        foreach (var item in list)
        {
            FilteredGames.Add(item);
        }
    }
}
