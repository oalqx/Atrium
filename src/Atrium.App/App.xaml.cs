using System;
using Atrium.App.ViewModels;
using Atrium.Core.Library;
using Atrium.Core.Storage;
using Atrium.Core.System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Atrium.App;

public partial class App : Application
{
    private Window? _window;
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += (sender, e) =>
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
            System.IO.File.AppendAllText(logPath, $"UNHANDLED XAML EXCEPTION:\n{e.Exception}\nMessage: {e.Message}\n");
            e.Handled = true;
        };

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Singletons
        services.AddSingleton<LibraryDatabase>();
        services.AddSingleton<LibrarySyncService>();
        services.AddSingleton<ILibrarySyncService>(provider => provider.GetRequiredService<LibrarySyncService>());
        services.AddSingleton<GpuPreferenceService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<GpuOptimizerViewModel>();
        services.AddSingleton<SettingsViewModel>();
    }

    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        System.IO.File.AppendAllText(logPath, "OnLaunched entered...\n");
        try
        {
            System.IO.File.AppendAllText(logPath, "Creating MainWindow...\n");
            _window = new MainWindow();
            System.IO.File.AppendAllText(logPath, "Activating MainWindow...\n");
            _window.Activate();
#if UI_SMOKE_TEST
            _ = ((MainWindow)_window).RunUiSmokeAsync();
#endif
            System.IO.File.AppendAllText(logPath, "MainWindow activated successfully!\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(logPath, $"EXCEPTION in OnLaunched:\n{ex}\n");
        }
    }
}
