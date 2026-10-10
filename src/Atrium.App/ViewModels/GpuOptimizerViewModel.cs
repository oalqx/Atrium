using System.Collections.ObjectModel;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using Atrium.Core.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atrium.App.ViewModels;

public sealed partial class GameGpuItemViewModel : ObservableObject
{
    private readonly GpuPreferenceService _gpuService;

    public string Title { get; }
    public string ExePath { get; }
    public StoreKind Store { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePreference))]
    [NotifyPropertyChangedFor(nameof(PreferenceDescription))]
    private bool _isHighPerformance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePreference))]
    [NotifyPropertyChangedFor(nameof(PreferenceDescription))]
    private bool _isManagedByAtrium;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePreference))]
    private bool _isChangingPreference;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool CanChangePreference => !IsChangingPreference && (!IsHighPerformance || IsManagedByAtrium);
    public string PreferenceDescription => IsManagedByAtrium ? "Managed by Atrium" :
        IsHighPerformance ? "Already configured in Windows" : "Using Windows graphics preference";

    public GameGpuItemViewModel(string title, string exePath, StoreKind store, bool isHighPerf, GpuPreferenceService gpuService, bool isManagedByAtrium = false)
    {
        Title = title;
        ExePath = exePath;
        Store = store;
        _isHighPerformance = isHighPerf;
        _gpuService = gpuService;
        _isManagedByAtrium = isManagedByAtrium;
    }

    [RelayCommand]
    public async Task ToggleGpuPreferenceAsync()
    {
        if (!CanChangePreference) return;
        IsChangingPreference = true;
        ErrorMessage = string.Empty;
        try
        {
            if (IsHighPerformance)
                await _gpuService.RevertAsync(ExePath);
            else
                await _gpuService.SetHighPerformanceAsync(ExePath);
            var modified = await _gpuService.GetModifiedExePathsAsync();
            IsManagedByAtrium = modified.Contains(Path.GetFullPath(ExePath), StringComparer.OrdinalIgnoreCase);
            IsHighPerformance = _gpuService.GetPreference(ExePath) == GpuPreference.HighPerformance;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not change GPU preference: {ex.Message}";
        }
        finally { IsChangingPreference = false; }
    }
}

public sealed partial class GpuOptimizerViewModel : ObservableObject
{
    private readonly LibraryDatabase _database;
    private readonly GpuPreferenceService _gpuService;

    [ObservableProperty]
    private string _discreteGpuName = "Detecting...";

    [ObservableProperty]
    private string _discreteVramText = string.Empty;

    [ObservableProperty]
    private string _integratedGpuName = "None";

    [ObservableProperty]
    private string _cpuName = "Detecting...";

    [ObservableProperty]
    private string _ramText = string.Empty;

    [ObservableProperty]
    private string _displayText = string.Empty;

    [ObservableProperty]
    private bool _hasDualGpu;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OptimizeAllGamesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertAllCommand))]
    private bool _isOptimizing;

    private bool CanOptimize() => !IsOptimizing;

    public ObservableCollection<GameGpuItemViewModel> InstalledGameItems { get; } = [];

    public GpuOptimizerViewModel(LibraryDatabase database, GpuPreferenceService gpuService)
    {
        _database = database;
        _gpuService = gpuService;
    }

    public async Task InitializeAsync()
    {
        ApplyHardware(await Task.Run(HardwareService.Detect));
        await LoadInstalledGamesAsync();
    }

    private void ApplyHardware(HardwareInfo hw)
    {
        CpuName = hw.CpuName;
        RamText = $"{hw.TotalRamGigabytes} GB RAM";
        DisplayText = $"{hw.PrimaryDisplay.Width}x{hw.PrimaryDisplay.Height} @ {hw.PrimaryDisplay.RefreshRateHz}Hz";

        var discrete = hw.Gpus.FirstOrDefault(g => g.IsHighPerformance) ?? hw.Gpus.FirstOrDefault();
        var integrated = hw.Gpus.FirstOrDefault(g => !g.IsHighPerformance && g != discrete);

        if (discrete != null)
        {
            DiscreteGpuName = discrete.Name;
            DiscreteVramText = $"{discrete.DedicatedVramGigabytes:F1} GB VRAM";
        }

        if (integrated != null)
        {
            IntegratedGpuName = integrated.Name;
            HasDualGpu = true;
        }
        else
        {
            HasDualGpu = hw.Gpus.Count > 1;
        }
    }

    [RelayCommand]
    public async Task LoadInstalledGamesAsync()
    {
        InstalledGameItems.Clear();
        var games = await _database.GetAllGamesAsync();
        var modified = (await _gpuService.GetModifiedExePathsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var savedStore = await _database.GetSettingAsync("PreferredStore", "Steam");
        var preferredStore = Enum.TryParse<StoreKind>(savedStore, out var store) ? store : StoreKind.Steam;
        var storeOrder = PreferredSourceResolver.CreateStoreOrder(preferredStore);

        foreach (var g in games.Where(g => g.IsInstalled))
        {
            var primary = PreferredSourceResolver.Resolve(g, storeOrder);
            var source = primary is { IsInstalled: true } && File.Exists(primary.ExecutablePath)
                ? primary : g.Sources.FirstOrDefault(s => s.IsInstalled && File.Exists(s.ExecutablePath));
            var exe = source?.ExecutablePath;

            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var pref = _gpuService.GetPreference(exe);
                bool isHigh = pref == GpuPreference.HighPerformance;

                InstalledGameItems.Add(new GameGpuItemViewModel(
                    title: g.Title,
                    exePath: exe,
                    store: source!.Store,
                    isHighPerf: isHigh,
                    gpuService: _gpuService,
                    isManagedByAtrium: modified.Contains(Path.GetFullPath(exe))));
            }
        }

        int optimizedCount = InstalledGameItems.Count(i => i.IsHighPerformance);
        StatusMessage = $"{InstalledGameItems.Count} games with verified executables ({optimizedCount} forced to high-performance GPU).";
    }

    [RelayCommand(CanExecute = nameof(CanOptimize))]
    public async Task OptimizeAllGamesAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        StatusMessage = "Setting high-performance GPU preferences...";
        int count = 0, failed = 0;
        string? firstError = null;
        try
        {
            foreach (var item in InstalledGameItems.ToList())
            {
                try
                {
                    if (await _gpuService.SetHighPerformanceAsync(item.ExePath)) count++;
                }
                catch (Exception ex) { failed++; firstError ??= ex.Message; }
            }
            await LoadInstalledGamesAsync();
            StatusMessage = failed == 0 ? $"Updated {count} GPU preferences." :
                $"Updated {count} preferences; {failed} failed. {firstError}";
        }
        catch (Exception ex) { StatusMessage = $"Could not complete optimization: {ex.Message}"; }
        finally { IsOptimizing = false; }
    }

    [RelayCommand(CanExecute = nameof(CanOptimize))]
    public async Task RevertAllAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        StatusMessage = "Restoring preferences changed by Atrium...";
        int count = 0, failed = 0;
        string? firstError = null;
        try
        {
            // Include backed-up executables even when the game was uninstalled.
            foreach (var path in await _gpuService.GetModifiedExePathsAsync())
            {
                try
                {
                    if (await _gpuService.RevertAsync(path)) count++;
                }
                catch (Exception ex) { failed++; firstError ??= ex.Message; }
            }
            await LoadInstalledGamesAsync();
            StatusMessage = failed == 0 ? $"Restored {count} preferences changed by Atrium." :
                $"Restored {count} preferences; {failed} failed and their backups were kept. {firstError}";
        }
        catch (Exception ex) { StatusMessage = $"Could not complete rollback: {ex.Message}"; }
        finally { IsOptimizing = false; }
    }
}
