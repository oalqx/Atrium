using System.Text.Json;
using Atrium.App.ViewModels;
using Atrium.App.Views;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Microsoft.UI.Input;

namespace Atrium.App;

// Compiled only with -p:UiSmokeTest=true. Exercises native XAML bindings
// against the current library without syncing or saving fixture games.
public sealed partial class MainWindow
{
    internal async Task RunUiSmokeAsync()
    {
        var errors = new List<string>();
        int loadedGames = 0, realizedCards = 0;
        bool imageFailureFallback = false, libraryControls = false, detailsBindings = false, compactLayout = false;
        bool themedTitleBar = false, sourcesControls = false;
        var controlsOnly = Environment.GetCommandLineArgs().Contains("--controls-only");
        var reportPath = Path.Combine(AppContext.BaseDirectory, "ui-smoke.json");
        var corruptCover = Path.Combine(AppContext.BaseDirectory, "invalid-smoke-cover.png");
        Application.Current.UnhandledException += (_, args) => errors.Add(args.Exception.ToString());
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_initializationTask == null && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (_initializationTask == null) throw new InvalidOperationException("Startup did not begin.");
            await _initializationTask.WaitAsync(TimeSpan.FromSeconds(30));
            if (!ExtendsContentIntoTitleBar) errors.Add("The default Windows title bar was not replaced.");
            var originalTheme = (Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                SetTheme(theme);
                await Task.Delay(150);
                AppTitleBar.UpdateLayout();
                var expectedForeground = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
                if (AppWindow.TitleBar.ButtonForegroundColor != expectedForeground ||
                    AppWindow.TitleBar.ButtonBackgroundColor?.A != 0 ||
                    (theme == ElementTheme.Dark && AppWindow.TitleBar.BackgroundColor?.R > 64))
                    errors.Add($"The title bar did not follow the {theme} app theme.");
            }
            SetTheme(originalTheme);
            NavView.SelectedItem = NavView.MenuItems[1];
            await Task.Delay(150);
            var sourcesPage = ContentFrame.Content as SourcesPage
                ?? throw new InvalidOperationException("Sources page was not created.");
            var sources = ViewModel.SourcesVM;
            var originalStatus = sources.SyncStatusText;
            sources.IsSyncing = true;
            sources.SyncProgressPercent = 50;
            sources.IsProgressIndeterminate = false;
            sources.SyncStatusText = "Scanning test provider...";
            await Task.Delay(150);
            sourcesPage.UpdateLayout();
            var progressBar = sourcesPage.FindName("SyncProgressBar") as ProgressBar;
            var cancelButton = sourcesPage.FindName("CancelSyncButton") as Button;
            var syncButton = sourcesPage.FindName("SyncButton") as Button;
            if (progressBar?.Value != 50 || progressBar.Visibility != Visibility.Visible ||
                cancelButton?.Visibility != Visibility.Visible || cancelButton.IsEnabled || syncButton?.IsEnabled != false ||
                ViewModel.SyncLibraryCommand.CanExecute(null))
                errors.Add("The native Sources sync controls did not reflect the running scan.");
            var regions = InputNonClientPointerSource.GetForWindowId(AppWindow.Id).GetRegionRects(NonClientRegionKind.Passthrough);
            if (regions.Length == 0 || regions[0].Width <= 0 ||
                TitleBarActions.ActualWidth <= 0 || RightCaptionInset.Width.Value <= 0)
                errors.Add("The title bar did not reserve interactive and caption-button regions.");
            sources.IsCancelling = true;
            await Task.Delay(100);
            if (cancelButton?.IsEnabled != false) errors.Add("Cancel stayed enabled after cancellation was requested.");
            sources.IsSyncing = false;
            sources.IsCancelling = false;
            sources.SyncProgressPercent = 0;
            sources.SyncStatusText = originalStatus;
            await Task.Delay(100);
            if (cancelButton?.Visibility != Visibility.Collapsed || syncButton?.IsEnabled != true ||
                !ViewModel.SyncLibraryCommand.CanExecute(null))
                errors.Add("Sync controls did not reset after a scan.");
            themedTitleBar = errors.Count == 0;
            sourcesControls = errors.Count == 0;
            NavView.SelectedItem = NavView.MenuItems[0];
            await Task.Delay(150);
            loadedGames = ViewModel.LibraryVM.FilteredGames.Count;
            var library = ViewModel.LibraryVM;
            var savedGames = await App.GetService<LibraryDatabase>().GetAllGamesAsync();
            var libraryPage = ContentFrame.Content as LibraryPage
                ?? throw new InvalidOperationException("Initial Library page was not created.");
            library.SearchText = $"no-game-matches-{Guid.NewGuid():N}";
            await Task.Delay(100);
            libraryPage.UpdateLayout();
            if (!library.ShowEmptyState || library.FilteredGames.Count != 0 ||
                (libraryPage.FindName("EmptyPanel") as FrameworkElement)?.Visibility != Visibility.Visible)
                errors.Add("An unmatched search did not show the native empty state.");
            library.ResetFilters();
            if (library.FilteredGames.Count != loadedGames || library.HasActiveFilters)
                errors.Add("Clearing filters did not restore the library.");
            library.SelectedFilter = "Hidden";
            if (library.FilteredGames.Count != savedGames.Count(g => g.User.Hidden))
                errors.Add("The Hidden filter did not match the saved hidden games.");
            library.SelectedFilter = "Installed";
            if (library.FilteredGames.Count != savedGames.Count(g => !g.User.Hidden && g.IsInstalled))
                errors.Add("The Installed filter did not match visible installed games.");
            library.ResetFilters();
            library.SelectedSort = "Recently Played";
            var expectedOrder = savedGames.Where(g => !g.User.Hidden).OrderByDescending(g => g.MostRecentPlayed)
                .ThenBy(g => g.SortTitle).Select(g => g.Id);
            if (!library.FilteredGames.Select(g => g.Id).SequenceEqual(expectedOrder))
                errors.Add("Recently Played order did not match the saved activity.");
            library.SelectedSort = "Title (A-Z)";
            libraryControls = errors.Count == 0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 700));
            await Task.Delay(250);
            libraryPage.UpdateLayout();
            foreach (var name in new[] { "SearchBox", "FilterBox", "SortBox" })
            {
                var control = libraryPage.FindName(name) as FrameworkElement
                    ?? throw new InvalidOperationException($"{name} was not created.");
                var position = control.TransformToVisual(libraryPage).TransformPoint(new Windows.Foundation.Point());
                if (position.X < 0 || position.X + control.ActualWidth > libraryPage.ActualWidth + 1)
                    errors.Add($"{name} overflowed the compact Library viewport.");
            }
            foreach (var game in library.FilteredGames.Take(3).ToList())
            {
                library.SelectGame(game);
                await Task.Delay(100);
                libraryPage.UpdateLayout();
                var choices = libraryPage.FindName("LaunchPreferenceBox") as ComboBox;
                if ((libraryPage.FindName("DetailsOverlay") as FrameworkElement)?.Visibility != Visibility.Visible ||
                    choices?.Items.Count != game.StorePreferences.Count ||
                    !Equals(choices.SelectedItem, game.SelectedStorePreference))
                    errors.Add($"Game details did not bind the correct launch preference for '{game.Title}': " +
                        $"items={choices?.Items.Count}/{game.StorePreferences.Count}, selected={choices?.SelectedItem}, " +
                        $"expected={game.SelectedStorePreference}, draft={game.PendingStorePreference}.");
                var details = libraryPage.FindName("DetailsCard") as FrameworkElement;
                if (details == null || details.ActualWidth > libraryPage.ActualWidth || details.ActualHeight > libraryPage.ActualHeight)
                    errors.Add("The details panel overflowed the compact Library viewport.");
                library.CloseDetails();
                await Task.Delay(50);
            }
            detailsBindings = errors.Count == 0;
            compactLayout = errors.Count == 0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 900));
            await Task.Delay(250);
            File.WriteAllText(corruptCover, "This is deliberately not an image.");
            var db = App.GetService<LibraryDatabase>();
            var brokenCoverCard = new GameCardViewModel(new Game
            {
                Title = "Smoke test: corrupt local image",
                Artwork = new ArtworkSet { LocalCoverPath = corruptCover }
            }, db);
            ViewModel.LibraryVM.FilteredGames.Insert(0, brokenCoverCard);
            ViewModel.LibraryVM.FilteredGames.Insert(1, new GameCardViewModel(new Game { Title = "Smoke test: missing image" }, db));
            ViewModel.LibraryVM.FilteredGames.Insert(2, new GameCardViewModel(new Game
            {
                Title = "Smoke test: invalid image URI", Artwork = new ArtworkSet { CoverUrl = "not a URI" }
            }, db));

            for (int pass = 0; !controlsOnly && pass < 2; pass++)
            {
                NavView.SelectedItem = NavView.MenuItems[1];
                await Task.Delay(250);
                NavView.SelectedItem = NavView.MenuItems[0];
                await Task.Delay(500);
                var page = ContentFrame.Content as LibraryPage
                    ?? throw new InvalidOperationException("Library navigation failed.");
                var grid = page.FindName("GamesGrid") as GridView
                    ?? throw new InvalidOperationException("Game grid was not created.");
                foreach (var card in ViewModel.LibraryVM.FilteredGames.ToList())
                {
                    grid.ScrollIntoView(card);
                    await Task.Delay(35);
                    grid.UpdateLayout();
                    if (grid.ContainerFromItem(card) != null) realizedCards++;
                }
                grid.ScrollIntoView(brokenCoverCard);
                await Task.Delay(500);
            }
            imageFailureFallback = brokenCoverCard.CoverLoadFailed && !brokenCoverCard.HasCover;
            if (!controlsOnly && !imageFailureFallback) errors.Add("A corrupt cover did not show its title fallback.");
            if (!controlsOnly && realizedCards == 0) errors.Add("No native game-card containers were realized.");
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        finally
        {
            File.Delete(corruptCover);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                Passed = errors.Count == 0,
                Scope = controlsOnly ? "Controls" : "Full",
                LoadedGames = loadedGames,
                RealizedCards = realizedCards,
                ImageFailureFallback = imageFailureFallback,
                LibraryControls = libraryControls,
                DetailsBindings = detailsBindings,
                CompactLayout = compactLayout,
                ThemedTitleBar = themedTitleBar,
                SourcesControls = sourcesControls,
                Errors = errors
            }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
