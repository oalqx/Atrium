using Atrium.Core.Models;
using Atrium.Core.Storage;
using Atrium.Core.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed record StorePreferenceOption(StoreKind? Store, string Label);
public sealed record LaunchSourceOption(GameSource Source, string Label, string Status);

public sealed partial class GameCardViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly IGameLaunchService _launcher;
    private IReadOnlyList<StoreKind>? _storeOrder;

    public Game Game { get; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideActionLabel))]
    private bool _isHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleHiddenCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePreferredStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCollectionCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCollectionCommand))]
    private string _collectionDraft = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePreferredStoreCommand))]
    private StorePreferenceOption? _pendingStorePreference;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionMessage))]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private bool _coverLoadFailed;

    public Guid Id => Game.Id;
    public string Title => Game.Title;
    public string? CoverUrl => Game.Artwork.EffectiveCover;
    public Uri? CoverUri => ArtworkUriResolver.Resolve(CoverUrl);
    public string? BackgroundUrl => Game.Artwork.EffectiveBackground;
    public Uri? BackgroundUri => ArtworkUriResolver.Resolve(BackgroundUrl) ?? CoverUri;
    public string LastPlayedText => Game.MostRecentPlayed?.ToLocalTime().ToString("MMM d, yyyy") ?? "No activity recorded";
    public IReadOnlyList<LaunchSourceOption> LaunchSources => Game.Sources.Select(source =>
        new LaunchSourceOption(source, StoreName(source.Store), source.IsInstalled ? "Installed" : "In your library")).ToArray();
    public bool HasCover => CoverUri != null && !CoverLoadFailed;
    public int PlaytimeMinutes => Game.TotalPlaytimeMinutes;
    public IReadOnlyList<GameSource> Sources => Game.Sources;
    public GameSource? PrimarySource => PreferredSourceResolver.Resolve(Game, _storeOrder);

    public bool CanEdit => !IsSaving;
    public bool CanAddCollection => CanEdit && !string.IsNullOrWhiteSpace(CollectionDraft);
    public IReadOnlyList<string> Collections => Game.User.Collections;
    public bool HasCollections => Collections.Count > 0;
    public bool CanSavePreference => CanEdit && PendingStorePreference != null &&
        PendingStorePreference.Store != Game.User.PreferredStoreOverride;
    public bool HasActionMessage => !string.IsNullOrEmpty(ActionMessage);
    public string HideActionLabel => IsHidden ? "Restore to library" : "Hide from library";
    public string FavoriteActionLabel => IsFavorite ? "Remove favorite" : "Add favorite";
    public string LaunchActionLabel => PrimarySource?.IsInstalled == true ? $"Play via {PrimaryStoreName}" : $"Open in {PrimaryStoreName}";
    public string InstallationStatus => IsInstalled ? "Installed" : "Not installed";
    public IReadOnlyList<StorePreferenceOption> StorePreferences { get; }
    public StorePreferenceOption SelectedStorePreference =>
        StorePreferences.FirstOrDefault(p => p.Store == Game.User.PreferredStoreOverride) ?? StorePreferences[0];

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

    public GameCardViewModel(Game game, LibraryDatabase database, IReadOnlyList<StoreKind>? storeOrder = null,
        IGameLaunchService? launcher = null)
    {
        Game = game;
        _database = database;
        _launcher = launcher ?? new GameLaunchService();
        _storeOrder = storeOrder;
        _isFavorite = game.User.Favorite;
        _isInstalled = game.IsInstalled;
        _isHidden = game.User.Hidden;
        StorePreferences = new[] { new StorePreferenceOption(null, "Follow library preference") }
            .Concat(game.Sources.Select(s => s.Store).Distinct()
                .Select(store => new StorePreferenceOption(store, StoreName(store)))).ToArray();
        _pendingStorePreference = SelectedStorePreference;
    }

    public void UpdateStoreOrder(IReadOnlyList<StoreKind> storeOrder)
    {
        _storeOrder = storeOrder;
        NotifyLaunchSourceChanged();
    }

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteActionLabel));

    private void NotifyLaunchSourceChanged()
    {
        OnPropertyChanged(nameof(PrimarySource));
        OnPropertyChanged(nameof(PrimaryStoreName));
        OnPropertyChanged(nameof(LaunchActionLabel));
        OnPropertyChanged(nameof(SelectedStorePreference));
    }

    private static string StoreName(StoreKind store) => store switch
    {
        StoreKind.Steam => "Steam",
        StoreKind.Epic => "Epic Games",
        StoreKind.Gog => "GOG",
        StoreKind.Xbox => "Xbox",
        StoreKind.Local => "Local PC",
        _ => store.ToString()
    };

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task ToggleFavoriteAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        ActionMessage = string.Empty;
        try
        {
            var value = !IsFavorite;
            await _database.SetFavoriteAsync(Game.Id, value);
            Game.User.Favorite = value;
            IsFavorite = value;
        }
        catch (Exception ex) { ActionMessage = $"Could not save favorite: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task ToggleHiddenAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        ActionMessage = string.Empty;
        try
        {
            var value = !IsHidden;
            await _database.SetHiddenAsync(Game.Id, value);
            Game.User.Hidden = value;
            IsHidden = value;
            ActionMessage = value ? "Game hidden. Find it in the Hidden filter to restore it." : "Game restored to your library.";
        }
        catch (Exception ex) { ActionMessage = $"Could not save visibility: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    public async Task SetPreferredStoreAsync(StorePreferenceOption? option)
    {
        if (option == null || !StorePreferences.Contains(option) || IsSaving ||
            option.Store == Game.User.PreferredStoreOverride) return;
        IsSaving = true;
        ActionMessage = string.Empty;
        try
        {
            await _database.SetPreferredStoreAsync(Game.Id, option.Store);
            Game.User.PreferredStoreOverride = option.Store;
        }
        catch (Exception ex) { ActionMessage = $"Could not save launcher preference: {ex.Message}"; }
        finally
        {
            PendingStorePreference = SelectedStorePreference;
            NotifyLaunchSourceChanged();
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSavePreference))]
    public Task SavePreferredStoreAsync() => SetPreferredStoreAsync(PendingStorePreference);

    [RelayCommand(CanExecute = nameof(CanAddCollection))]
    public Task AddCollectionAsync() => ChangeCollectionAsync(CollectionDraft, true);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public Task RemoveCollectionAsync(string? name) => ChangeCollectionAsync(name, false);

    private async Task ChangeCollectionAsync(string? name, bool add)
    {
        if (IsSaving || string.IsNullOrWhiteSpace(name)) return;
        IsSaving = true;
        ActionMessage = string.Empty;
        try
        {
            var saved = await _database.ChangeCollectionAsync(Game.Id, name, add);
            Game.User.Collections = saved.ToList();
            OnPropertyChanged(nameof(Collections));
            OnPropertyChanged(nameof(HasCollections));
            if (add) CollectionDraft = string.Empty;
            ActionMessage = add ? $"Added to {name.Trim()}." : $"Removed from {name.Trim()}.";
        }
        catch (Exception ex) { ActionMessage = $"Could not save collection: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    public void Launch()
    {
        var target = PrimarySource;
        ActionMessage = target == null
            ? "No launcher is linked to this game. Sync your library to refresh its sources."
            : _launcher.Launch(target).Message;
    }

    public void LaunchSource(GameSource source)
    {
        if (!Game.Sources.Contains(source)) return;
        ActionMessage = _launcher.Launch(source).Message;
    }
}
