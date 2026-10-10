using System;
using Atrium.App.ViewModels;
using Atrium.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        this.InitializeComponent();

        ViewModel.SettingsVM.ThemeChanged += OnThemeChanged;
        SetTheme(ViewModel.SettingsVM.CurrentTheme);

        // Default to LibraryPage
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(LibraryPage));

        // Start background initialization and initial sync check
        _ = ViewModel.InitializeAsync();
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        SetTheme(theme);
    }

    private void SetTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Type pageType = tag switch
            {
                "Library" => typeof(LibraryPage),
                "Sources" => typeof(SourcesPage),
                "GpuOptimizer" => typeof(GpuOptimizerPage),
                "Settings" => typeof(SettingsPage),
                _ => typeof(LibraryPage)
            };

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
