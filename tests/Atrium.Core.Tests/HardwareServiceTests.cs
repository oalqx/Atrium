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

        // If running on a multi-GPU system (such as user's laptop with NVIDIA RTX + Radeon iGPU)
        if (info.HasMultiGpu)
        {
            info.HighPerformanceGpu.Should().NotBeNull();
            info.PowerSavingGpu.Should().NotBeNull();
        }
    }
}
