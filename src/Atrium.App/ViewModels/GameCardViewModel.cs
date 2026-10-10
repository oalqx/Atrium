using System.Diagnostics;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed partial class GameCardViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;

    public Game Game { get; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isInstalled;

    public Guid Id => Game.Id;
    public string Title => Game.Title;
    public string? CoverUrl => Game.Artwork.EffectiveCover;
    public string? BackgroundUrl => Game.Artwork.EffectiveBackground;
    public bool HasCover => !string.IsNullOrEmpty(CoverUrl);
    public int PlaytimeMinutes => Game.TotalPlaytimeMinutes;
    public IReadOnlyList<GameSource> Sources => Game.Sources;
    public GameSource? PrimarySource => PreferredSourceResolver.Resolve(Game);

    public string PrimaryStoreName => PrimarySource != null ? PrimarySource.Store switch
    {
        StoreKind.Steam => "Steam",
        StoreKind.Epic => "Epic Games",
        StoreKind.Gog => "GOG",
        StoreKind.Xbox => "Xbox",
        StoreKind.Local => "PC Executable",
        _ => PrimarySource.Store.ToString()
    } : "Unknown";

    public IReadOnlyList<StoreKind> AvailableStores =>
        Game.Sources.Select(s => s.Store).Distinct().ToList();

    public GameCardViewModel(Game game, LibraryDatabase database)
    {
        Game = game;
        _database = database;
        _isFavorite = game.User.Favorite;
        _isInstalled = game.IsInstalled;
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync()
    {
        IsFavorite = !IsFavorite;
        Game.User.Favorite = IsFavorite;
        await _database.SetFavoriteAsync(Game.Id, IsFavorite);
    }

    [RelayCommand]
    public void Launch()
    {
        var target = PrimarySource;
        if (target == null) return;

        LaunchSource(target);
    }

    public static void LaunchSource(GameSource source)
    {
        try
        {
            if (!string.IsNullOrEmpty(source.LaunchUri))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = source.LaunchUri,
                    UseShellExecute = true
                });
                return;
            }

            if (!string.IsNullOrEmpty(source.ExecutablePath) && File.Exists(source.ExecutablePath))
            {
                var workDir = Path.GetDirectoryName(source.ExecutablePath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = source.ExecutablePath,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}
