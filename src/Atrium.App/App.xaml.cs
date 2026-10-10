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

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Singletons
        services.AddSingleton<LibraryDatabase>();
        services.AddSingleton<LibrarySyncService>();
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
        _window = new MainWindow();
        _window.Activate();
    }
}
