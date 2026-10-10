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
    private bool _isHighPerformance;

    public GameGpuItemViewModel(string title, string exePath, StoreKind store, bool isHighPerf, GpuPreferenceService gpuService)
    {
        Title = title;
        ExePath = exePath;
        Store = store;
        _isHighPerformance = isHighPerf;
        _gpuService = gpuService;
    }

    [RelayCommand]
    public async Task ToggleGpuPreferenceAsync()
    {
        if (IsHighPerformance)
        {
            await _gpuService.RevertAsync(ExePath);
            IsHighPerformance = false;
        }
        else
        {
            await _gpuService.SetHighPerformanceAsync(ExePath);
            IsHighPerformance = true;
        }
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
    private bool _isOptimizing;

    public ObservableCollection<GameGpuItemViewModel> InstalledGameItems { get; } = [];

    public GpuOptimizerViewModel(LibraryDatabase database, GpuPreferenceService gpuService)
    {
        _database = database;
        _gpuService = gpuService;
    }

    public async Task InitializeAsync()
    {
        DetectHardware();
        await LoadInstalledGamesAsync();
    }

    public void DetectHardware()
    {
        var hw = HardwareService.Detect();

        CpuName = hw.CpuName;
        RamText = $"{hw.TotalRamGigabytes} GB RAM";
        DisplayText = $"{hw.PrimaryDisplay.Width}x{hw.PrimaryDisplay.Height} @ {hw.PrimaryDisplay.RefreshRateHz}Hz";

        var discrete = hw.Gpus.FirstOrDefault(g => g.IsHighPerformance) ?? hw.Gpus.FirstOrDefault();
        var integrated = hw.Gpus.FirstOrDefault(g => !g.IsHighPerformance);

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

        foreach (var g in games.Where(g => g.IsInstalled))
        {
            var primary = PreferredSourceResolver.Resolve(g);
            var exe = primary?.ExecutablePath ?? g.Sources.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecutablePath))?.ExecutablePath;

            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var pref = _gpuService.GetPreference(exe);
                bool isHigh = pref == GpuPreference.HighPerformance;

                InstalledGameItems.Add(new GameGpuItemViewModel(
                    title: g.Title,
                    exePath: exe,
                    store: primary?.Store ?? StoreKind.Local,
                    isHighPerf: isHigh,
                    gpuService: _gpuService));
            }
        }

        int optimizedCount = InstalledGameItems.Count(i => i.IsHighPerformance);
        StatusMessage = $"{InstalledGameItems.Count} games with verified executables ({optimizedCount} forced to high-performance GPU).";
    }

    [RelayCommand]
    public async Task OptimizeAllGamesAsync()
    {
        IsOptimizing = true;
        StatusMessage = "Enforcing High-Performance GPU for all installed games...";

        int count = 0;
        foreach (var item in InstalledGameItems)
        {
            if (!item.IsHighPerformance)
            {
                await _gpuService.SetHighPerformanceAsync(item.ExePath);
                item.IsHighPerformance = true;
                count++;
            }
        }

        IsOptimizing = false;
        StatusMessage = $"Successfully optimized {count} new games! All {InstalledGameItems.Count} games now set to High-Performance GPU.";
    }

    [RelayCommand]
    public async Task RevertAllAsync()
    {
        IsOptimizing = true;
        StatusMessage = "Reverting GPU preferences to previous system state...";

        foreach (var item in InstalledGameItems)
        {
            if (item.IsHighPerformance)
            {
                await _gpuService.RevertAsync(item.ExePath);
                item.IsHighPerformance = false;
            }
        }

        IsOptimizing = false;
        StatusMessage = "All GPU preferences safely reverted to original defaults.";
    }
}
