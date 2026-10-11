using System;
using System.ComponentModel;
using Atrium.App.ViewModels;
using Atrium.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Atrium.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private readonly ArtworkThemeService _artworkThemes = new();
    private CancellationTokenSource? _artworkCancellation;

    public LibraryPage()
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        try
        {
            System.IO.File.AppendAllText(logPath, "LibraryPage ctor: resolving LibraryViewModel...\n");
            ViewModel = App.GetService<LibraryViewModel>();
            System.IO.File.AppendAllText(logPath, "LibraryPage ctor: InitializeComponent...\n");
            this.InitializeComponent();
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
            System.IO.File.AppendAllText(logPath, "LibraryPage ctor: initialized successfully!\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(logPath, $"EXCEPTION in LibraryPage ctor:\n{ex}\n");
            throw;
        }
    }

    private void OnGameClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameCardViewModel game)
        {
            ViewModel.SelectGame(game);
            DispatcherQueue.TryEnqueue(() => DetailsCloseButton.Focus(FocusState.Programmatic));
        }
    }

    private void OnCoverImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image { Tag: GameCardViewModel game }) game.CoverLoadFailed = true;
    }

    private void OnLibrarySizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 800;
        Grid.SetColumnSpan(SearchBox, compact ? 4 : 1);
        Grid.SetRow(FilterBox, compact ? 1 : 0);
        Grid.SetColumn(FilterBox, compact ? 0 : 1);
        Grid.SetColumnSpan(FilterBox, compact ? 2 : 1);
        Grid.SetRow(SortBox, compact ? 1 : 0);
        Grid.SetColumn(SortBox, 2);
        Grid.SetColumnSpan(SortBox, compact ? 2 : 1);
        Grid.SetRow(CollectionBox, compact ? 2 : 0);
        Grid.SetColumn(CollectionBox, compact ? 0 : 3);
        Grid.SetColumnSpan(CollectionBox, compact ? 4 : 1);
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnLibraryPropertyChanged;
        _ = UpdateArtworkThemeAsync(ViewModel.SelectedGame);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnLibraryPropertyChanged;
        _artworkCancellation?.Cancel();
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedGame))
        {
            LaunchScroll.ChangeView(null, 0, null, true);
            _ = UpdateArtworkThemeAsync(ViewModel.SelectedGame);
        }
    }

    private async Task UpdateArtworkThemeAsync(GameCardViewModel? game)
    {
        _artworkCancellation?.Cancel();
        ApplyArtworkPalette(ArtworkPalette.Default);
        if (game == null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _artworkCancellation = cancellation;
        try
        {
            var palette = await _artworkThemes.GetPaletteAsync(game.CoverUri ?? game.BackgroundUri, cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(game, ViewModel.SelectedGame))
                ApplyArtworkPalette(palette);
        }
        catch (OperationCanceledException) { }
        catch { /* Missing, offline, or corrupt art keeps the neutral launch theme. */ }
        finally { if (ReferenceEquals(_artworkCancellation, cancellation)) _artworkCancellation = null; }
    }

    private void ApplyArtworkPalette(ArtworkPalette palette)
    {
        static Windows.UI.Color Color(ArtworkColor value) => Windows.UI.Color.FromArgb(255, value.R, value.G, value.B);
        var surface = Color(palette.Surface);
        LaunchSurface.Background = new SolidColorBrush(surface);
        DetailsOverlay.Background = new SolidColorBrush(surface);
        HeroFadeStop.Color = surface;
        LaunchButton.Background = new SolidColorBrush(Color(palette.Accent));
        LaunchButton.Foreground = new SolidColorBrush(Color(palette.ButtonText));
        LaunchEyebrow.Foreground = new SolidColorBrush(Color(palette.Accent));
    }

    private void OnLaunchViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 720;
        var stacked = e.NewSize.Width < 440;
        LaunchPoster.Width = compact ? 120 : 180;
        LaunchPoster.Height = compact ? 180 : 270;
        LaunchGameTitle.FontSize = compact ? 28 : 36;
        HeroContent.Margin = new Thickness(compact ? 24 : 32, 80, compact ? 24 : 32, 32);
        Grid.SetColumn(LaunchIdentity, stacked ? 0 : 1);
        Grid.SetRow(LaunchIdentity, stacked ? 1 : 0);
        Grid.SetColumnSpan(LaunchIdentity, stacked ? 2 : 1);
        Grid.SetColumn(LaunchOrganizeCard, compact ? 0 : 1);
        Grid.SetRow(LaunchOrganizeCard, compact ? 1 : 0);
        Grid.SetColumnSpan(LaunchSourcesCard, compact ? 2 : 1);
        Grid.SetColumnSpan(LaunchOrganizeCard, compact ? 2 : 1);
    }

    private void OnBackdropImageFailed(object sender, ExceptionRoutedEventArgs e) => LaunchBackdrop.Opacity = 0;
    private void OnBackdropImageOpened(object sender, RoutedEventArgs e) => LaunchBackdrop.Opacity = 0.62;

    private void OnCollectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionBox.SelectedItem is CollectionFilterOption option && ViewModel.CollectionOptions.Contains(option))
            ViewModel.SelectedCollection = option;
    }

    private async void OnRemoveCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && ViewModel.SelectedGame is { } game)
            await game.RemoveCollectionAsync(name);
    }

    private async void OnCollectionNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.SelectedGame is { } game && game.AddCollectionCommand.CanExecute(null))
        {
            e.Handled = true;
            await game.AddCollectionAsync();
        }
    }

    private void OnSourceLaunchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameSource source }) ViewModel.SelectedGame?.LaunchSource(source);
    }

    private void OnSearchShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (!ViewModel.IsDetailsOpen)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnPreferenceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: StorePreferenceOption option } box &&
            ViewModel.SelectedGame is { } game && ReferenceEquals(box.ItemsSource, game.StorePreferences) &&
            game.StorePreferences.Contains(option))
            game.PendingStorePreference = option;
    }

    private void OnEscapeShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (ViewModel.IsDetailsOpen)
        {
            ViewModel.CloseDetails();
            GamesGrid.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }
}
