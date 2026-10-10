using Atrium.Core.Storage;
using Atrium.Core.System;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Atrium.Core.Tests;

public class GpuPreferenceServiceTests : IDisposable
{
    private const string TestSubKey = @"Software\AtriumTests\DirectX";
    private readonly string _testDbPath;
    private readonly string _fakeExePath;

    public GpuPreferenceServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"atrium_gpu_test_{Guid.NewGuid():N}.db");
        _fakeExePath = Path.Combine(Path.GetTempPath(), $"fakegame_{Guid.NewGuid():N}.exe");
        File.WriteAllText(_fakeExePath, "dummy binary");
    }

    public void Dispose()
    {
        if (File.Exists(_fakeExePath))
        {
            try { File.Delete(_fakeExePath); } catch { }
        }

        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\AtriumTests", false);
        }
        catch { }
    }

    [Fact]
    public async Task SetHighPerformance_AndRevert_RestoresOriginalState()
    {
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();

        var service = new GpuPreferenceService(db, TestSubKey);

        // Initially no preference
        service.GetPreference(_fakeExePath).Should().BeNull();

        // 1. Set High Performance
        await service.SetHighPerformanceAsync(_fakeExePath);
        service.GetPreference(_fakeExePath).Should().Be(GpuPreference.HighPerformance);

        var modified = await service.GetModifiedExePathsAsync();
        modified.Should().Contain(Path.GetFullPath(_fakeExePath));

        // 2. Revert back
        await service.RevertAsync(_fakeExePath);
        service.GetPreference(_fakeExePath).Should().BeNull();

        var afterRevert = await service.GetModifiedExePathsAsync();
        afterRevert.Should().NotContain(Path.GetFullPath(_fakeExePath));
    }
}
