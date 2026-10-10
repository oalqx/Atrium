namespace Atrium.Core.System;

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Unknown
}

public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    long DedicatedVideoMemoryBytes,
    bool IsHighPerformance)
{
    public double DedicatedVramGigabytes =>
        Math.Round((double)DedicatedVideoMemoryBytes / (1024 * 1024 * 1024), 1);
}

public sealed record DisplayInfo(
    int Width,
    int Height,
    int RefreshRateHz)
{
    public override string ToString() => $"{Width}x{Height} @ {RefreshRateHz}Hz";
}

public sealed record HardwareInfo(
    IReadOnlyList<GpuInfo> Gpus,
    string CpuName,
    int TotalRamGigabytes,
    DisplayInfo PrimaryDisplay)
{
    public bool HasMultiGpu => Gpus.Count > 1;

    public GpuInfo? HighPerformanceGpu =>
        Gpus.FirstOrDefault(g => g.IsHighPerformance) ??
        Gpus.OrderByDescending(g => g.DedicatedVideoMemoryBytes).FirstOrDefault();

    public GpuInfo? PowerSavingGpu =>
        Gpus.FirstOrDefault(g => !g.IsHighPerformance) ??
        Gpus.OrderBy(g => g.DedicatedVideoMemoryBytes).FirstOrDefault();
}
