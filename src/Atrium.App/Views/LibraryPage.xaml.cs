using System;
using Atrium.App.ViewModels;
using Atrium.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage()
    {
        ViewModel = App.GetService<LibraryViewModel>();
        this.InitializeComponent();
    }

    private async void OnGameClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameCardViewModel game)
        {
            ViewModel.SelectGame(game);
            GameDetailsModal.XamlRoot = this.XamlRoot;
            await GameDetailsModal.ShowAsync();
        }
    }

    private void OnPlayGameClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.SelectedGame?.Launch();
    }

    private void OnLaunchSpecificSourceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GameSource source)
        {
            GameCardViewModel.LaunchSource(source);
        }
    }
}
