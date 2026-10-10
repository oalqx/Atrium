using System;
using Atrium.App.ViewModels;
using Atrium.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Atrium.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage()
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        try
        {
            System.IO.File.AppendAllText(logPath, "LibraryPage ctor: resolving LibraryViewModel...\n");
            ViewModel = App.GetService<LibraryViewModel>();
            System.IO.File.AppendAllText(logPath, "LibraryPage ctor: InitializeComponent...\n");
            this.InitializeComponent();
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
        Grid.SetColumnSpan(SearchBox, compact ? 3 : 1);
        Grid.SetRow(FilterBox, compact ? 1 : 0);
        Grid.SetColumn(FilterBox, compact ? 0 : 1);
        Grid.SetRow(SortBox, compact ? 1 : 0);
        Grid.SetColumn(SortBox, compact ? 1 : 2);
        Grid.SetColumnSpan(SortBox, compact ? 2 : 1);
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
