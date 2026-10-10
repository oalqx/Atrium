using System;
using Atrium.App.ViewModels;
using Atrium.App.Views;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }
    private Task? _initializationTask;
    private Windows.Graphics.RectInt32? _titleBarInteractiveRegion;

    public MainWindow()
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        System.IO.File.AppendAllText(logPath, "MainWindow ctor: getting ViewModel...\n");
        ViewModel = App.GetService<MainWindowViewModel>();

        System.IO.File.AppendAllText(logPath, "MainWindow ctor: InitializeComponent...\n");
        this.InitializeComponent();
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            SetTitleBar(AppTitleBar);
            AppTitleBar.Loaded += (_, _) => UpdateTitleBarLayout();
            AppTitleBar.LayoutUpdated += (_, _) => UpdateTitleBarLayout();
            TitleBarActions.SizeChanged += (_, _) => UpdateTitleBarLayout();
        }

        System.IO.File.AppendAllText(logPath, "MainWindow ctor: Setting theme...\n");
        ViewModel.SettingsVM.ThemeChanged += OnThemeChanged;
        SetTheme(ViewModel.SettingsVM.CurrentTheme);

        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => ApplyTitleBarColors(root.ActualTheme);
            root.Loaded += OnRootLoaded;
        }

        System.IO.File.AppendAllText(logPath, "MainWindow ctor: completed.\n");
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        System.IO.File.AppendAllText(logPath, "OnRootLoaded: Window root loaded, setting NavView.SelectedItem...\n");

        if (sender is FrameworkElement root)
        {
            root.Loaded -= OnRootLoaded;
        }

        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        System.IO.File.AppendAllText(logPath, "OnRootLoaded: Initializing ViewModel async...\n");
        _initializationTask = ViewModel.InitializeAsync();
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
            ApplyTitleBarColors(root.ActualTheme);
        }
    }

    private void ApplyTitleBarColors(ElementTheme actualTheme)
    {
        if (!ExtendsContentIntoTitleBar) return;
        var dark = actualTheme == ElementTheme.Dark;
        var foreground = dark ? Colors.White : Colors.Black;
        var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var titleBar = AppWindow.TitleBar;
        titleBar.ForegroundColor = foreground;
        titleBar.BackgroundColor = background;
        titleBar.InactiveForegroundColor = dark ? Colors.DarkGray : Colors.Gray;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = dark ? Colors.DarkGray : Colors.Gray;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(25, 255, 255, 255) : Windows.UI.Color.FromArgb(20, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(45, 255, 255, 255) : Windows.UI.Color.FromArgb(35, 0, 0, 0);
    }

    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarLayout();

    private void UpdateTitleBarLayout()
    {
        if (!ExtendsContentIntoTitleBar || AppTitleBar.XamlRoot == null) return;
        var scale = AppTitleBar.XamlRoot.RasterizationScale;
        var leftInset = AppWindow.TitleBar.LeftInset / scale;
        var rightInset = AppWindow.TitleBar.RightInset / scale;
        if (Math.Abs(LeftCaptionInset.Width.Value - leftInset) > 0.01) LeftCaptionInset.Width = new GridLength(leftInset);
        if (Math.Abs(RightCaptionInset.Width.Value - rightInset) > 0.01) RightCaptionInset.Width = new GridLength(rightInset);
        var position = TitleBarActions.TransformToVisual(Content as UIElement)
            .TransformPoint(new Windows.Foundation.Point());
        var bounds = new Windows.Graphics.RectInt32(
            (int)Math.Floor(position.X * scale), (int)Math.Floor(position.Y * scale),
            (int)Math.Ceiling(TitleBarActions.ActualWidth * scale), (int)Math.Ceiling(TitleBarActions.ActualHeight * scale));
        if (bounds.Width > 0 && bounds.Height > 0 && !bounds.Equals(_titleBarInteractiveRegion))
        {
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [bounds]);
            _titleBarInteractiveRegion = bounds;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        System.IO.File.AppendAllText(logPath, $"OnNavSelectionChanged called.\n");

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
                System.IO.File.AppendAllText(logPath, $"ContentFrame.Navigate({pageType.Name})...\n");
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
