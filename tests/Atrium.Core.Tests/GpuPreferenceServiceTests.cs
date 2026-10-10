using Atrium.Core.Storage;
using Atrium.Core.System;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Atrium.Core.Tests;

public class GpuPreferenceServiceTests : IDisposable
{
    private readonly string _testSubKey = @"Software\AtriumTests\" + Guid.NewGuid().ToString("N");
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
            Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, false);
        }
        catch { }
    }

    [Fact]
    public async Task SetHighPerformance_AndRevert_RestoresOriginalState()
    {
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();

        var service = new GpuPreferenceService(db, _testSubKey);

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

    [Fact]
    public async Task Revert_LeavesExternalPreferencesUntouched_WhenThereIsNoBackup()
    {
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();
        var service = new GpuPreferenceService(db, _testSubKey);
        using var key = Registry.CurrentUser.CreateSubKey(_testSubKey);
        key.SetValue(_fakeExePath, "GpuPreference=2;CustomFlag=1;");

        await service.RevertAsync(_fakeExePath);
        await service.SetHighPerformanceAsync(_fakeExePath);

        key.GetValue(_fakeExePath).Should().Be("GpuPreference=2;CustomFlag=1;");
        (await service.GetModifiedExePathsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("GpuPreference=1;CustomFlag=1;")]
    [InlineData("")]
    public async Task RepeatedOptimization_RestoresTheExactOriginalValue(string original)
    {
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();
        var service = new GpuPreferenceService(db, _testSubKey);
        using var key = Registry.CurrentUser.CreateSubKey(_testSubKey);
        key.SetValue(_fakeExePath, original);

        await Task.WhenAll(service.SetHighPerformanceAsync(_fakeExePath), service.SetHighPerformanceAsync(_fakeExePath));
        if (original.Contains("CustomFlag"))
            key.GetValue(_fakeExePath)!.ToString().Should().Contain("CustomFlag=1;");
        await service.RevertAsync(_fakeExePath);

        key.GetValue(_fakeExePath).Should().Be(original);
        (await service.GetModifiedExePathsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedRegistryRestore_KeepsTheBackupForRetry()
    {
        using var db = new LibraryDatabase(_testDbPath);
        await db.InitializeAsync();
        var service = new GpuPreferenceService(db, _testSubKey);
        await service.SetHighPerformanceAsync(_fakeExePath);
        // Windows rejects registry key segments longer than 255 characters.
        var invalidRegistry = new GpuPreferenceService(db, new string('x', 256));
        Func<Task> restore = () => invalidRegistry.RevertAsync(_fakeExePath);

        await restore.Should().ThrowAsync<ArgumentException>();

        (await service.GetModifiedExePathsAsync()).Should().Contain(Path.GetFullPath(_fakeExePath));
        await service.RevertAsync(_fakeExePath);
        (await service.GetModifiedExePathsAsync()).Should().BeEmpty();
    }
}
