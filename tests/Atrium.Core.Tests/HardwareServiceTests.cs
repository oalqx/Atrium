using Atrium.Core.System;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class HardwareServiceTests
{
    [Fact]
    public void Detect_ReturnsAccurateHardwareSpecifications()
    {
        var info = HardwareService.Detect();

        info.Should().NotBeNull();
        info.Gpus.Should().NotBeEmpty();
        info.CpuName.Should().NotBeNullOrWhiteSpace();
        info.TotalRamGigabytes.Should().BeGreaterThan(0);
        info.PrimaryDisplay.Width.Should().BeGreaterThan(0);
        info.PrimaryDisplay.Height.Should().BeGreaterThan(0);
        info.PrimaryDisplay.RefreshRateHz.Should().BeGreaterThan(0);

        // On this specific machine, verify multi-GPU detection (RTX 4050 + AMD Radeon)
        info.HasMultiGpu.Should().BeTrue();
        info.HighPerformanceGpu.Should().NotBeNull();
        info.HighPerformanceGpu!.Vendor.Should().Be(GpuVendor.Nvidia);
        info.HighPerformanceGpu.Name.Should().Contain("RTX 4050");
    }
}
