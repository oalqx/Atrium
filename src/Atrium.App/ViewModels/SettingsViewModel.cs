using System.Diagnostics;
using System.Reflection;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Velopack;

namespace Atrium.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;

    [ObservableProperty]
    private ElementTheme _currentTheme = ElementTheme.Default;

    [ObservableProperty]
    private string _themeSelection = "Follow Windows";

    [ObservableProperty]
    private StoreKind _preferredDefaultStore = StoreKind.Steam;

    [ObservableProperty]
    private string _updateStatusText = "Up to date";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public string AppVersion { get; }

    public IReadOnlyList<string> ThemeOptions { get; } =
    [
        "Follow Windows",
        "Dark",
        "Light"
    ];

    public IReadOnlyList<StoreKind> StoreOptions { get; } =
    [
        StoreKind.Steam,
        StoreKind.Epic,
        StoreKind.Gog,
        StoreKind.Xbox,
        StoreKind.Local
    ];

    public event Action<ElementTheme>? ThemeChanged;

    public SettingsViewModel(LibraryDatabase database)
    {
        _database = database;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v0.1.0";
    }

    public async Task InitializeAsync()
    {
        var savedTheme = await _database.GetSettingAsync("AppTheme", "Follow Windows");
        ThemeSelection = savedTheme ?? "Follow Windows";

        var savedStore = await _database.GetSettingAsync("PreferredStore", "Steam");
        if (Enum.TryParse<StoreKind>(savedStore, out var store))
        {
            PreferredDefaultStore = store;
        }
    }

    partial void OnThemeSelectionChanged(string value)
    {
        CurrentTheme = value switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        _ = _database.SetSettingAsync("AppTheme", value);
        ThemeChanged?.Invoke(CurrentTheme);
    }

    partial void OnPreferredDefaultStoreChanged(StoreKind value)
    {
        _ = _database.SetSettingAsync("PreferredStore", value.ToString());
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;

        IsCheckingForUpdates = true;
        UpdateStatusText = "Checking GitHub Releases for updates...";

        try
        {
            var mgr = new UpdateManager(new Velopack.Sources.GithubSource("https://github.com/oalqx/Atrium", string.Empty, false));
            var newVersion = await mgr.CheckForUpdatesAsync();

            if (newVersion == null)
            {
                UpdateStatusText = "Atrium is up to date!";
            }
            else
            {
                UpdateStatusText = $"New version available: {newVersion.TargetFullRelease.Version}. Downloading...";
                await mgr.DownloadUpdatesAsync(newVersion);
                UpdateStatusText = "Update downloaded! Will apply when Atrium restarts.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Could not check updates: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/oalqx/Atrium",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
