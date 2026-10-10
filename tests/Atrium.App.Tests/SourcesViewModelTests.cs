using Atrium.App.ViewModels;
using Atrium.Core.Library;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using FluentAssertions;
using Xunit;

namespace Atrium.App.Tests;

public sealed class SourcesViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"atrium_sources_{Guid.NewGuid():N}");
    public SourcesViewModelTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CancelSync_StopsTheScan_ReenablesCommands_AndSkipsRefresh()
    {
        using var db = new LibraryDatabase(Path.Combine(_directory, "library.db"));
        await db.InitializeAsync();
        var service = new WaitingSync();
        var vm = new SourcesViewModel(db, service);
        var refreshes = 0;
        vm.SyncCompleted += () => { refreshes++; return Task.CompletedTask; };
        var run = vm.SyncAllAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.IsSyncing.Should().BeTrue();
        vm.CanEditFolders.Should().BeFalse();
        vm.SyncAllCommand.CanExecute(null).Should().BeFalse();

        vm.CancelSync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        refreshes.Should().Be(0);
        vm.IsSyncing.Should().BeFalse();
        vm.IsCancelling.Should().BeFalse();
        vm.CanEditFolders.Should().BeTrue();
        vm.CancelSyncCommand.CanExecute(null).Should().BeFalse();
        vm.SyncStatusText.Should().Contain("cancelled").And.Contain("unchanged");
    }

    [Fact]
    public async Task FailedScan_ShowsError_AndAllowsAnotherAttempt()
    {
        using var db = new LibraryDatabase(Path.Combine(_directory, "library.db"));
        var vm = new SourcesViewModel(db, new FailingSync());
        await vm.SyncAllAsync();
        vm.SyncStatusText.Should().Contain("disk failure");
        vm.CanEditFolders.Should().BeTrue();
        vm.SyncAllCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task CompletedSync_WithARefreshFailure_StillReportsSavedDataAndProviderIssues()
    {
        using var db = new LibraryDatabase(Path.Combine(_directory, "library.db"));
        await db.InitializeAsync();
        var vm = new SourcesViewModel(db, new CompletedSync());
        vm.SyncCompleted += () => throw new IOException("view refresh failed");

        await vm.SyncAllAsync();

        vm.SyncProgressPercent.Should().Be(100);
        vm.HasSyncIssues.Should().BeTrue();
        vm.SyncIssues.Should().ContainSingle().Which.Provider.Should().Be("Unavailable store");
        vm.SyncStatusText.Should().Contain("Sync complete").And.Contain("Library saved").And.Contain("view refresh failed");
        vm.CanCancelSync.Should().BeFalse();
        vm.CanEditFolders.Should().BeTrue();
    }

    [Fact]
    public async Task FolderPaths_AreNormalized_AndDuplicateOrMissingFoldersAreExplained()
    {
        using var db = new LibraryDatabase(Path.Combine(_directory, "library.db"));
        await db.InitializeAsync();
        var vm = new SourcesViewModel(db, new FailingSync());
        (await vm.AddCustomFolderAsync(_directory + Path.DirectorySeparatorChar)).Should().BeTrue();
        (await vm.AddCustomFolderAsync(_directory.ToUpperInvariant())).Should().BeFalse();
        vm.CustomFolders.Should().ContainSingle().Which.Should().Be(_directory);
        vm.FolderStatusText.Should().Contain("already configured");
        (await vm.AddCustomFolderAsync(Path.Combine(_directory, "not-there"))).Should().BeFalse();
        vm.FolderStatusText.Should().Contain("could not be found");
        (await db.GetSettingAsync("CustomFolders")).Should().Contain(_directory.Replace("\\", "\\\\"));
        await vm.RemoveCustomFolderAsync(_directory);
        vm.CustomFolders.Should().BeEmpty();
        (await db.GetSettingAsync("CustomFolders")).Should().Be("[]");
    }

    [Fact]
    public async Task FailedFolderSave_DoesNotChangeTheVisibleFolderList()
    {
        using var db = new LibraryDatabase(Path.Combine(_directory, "missing-schema.db"));
        var vm = new SourcesViewModel(db, new FailingSync());
        (await vm.AddCustomFolderAsync(_directory)).Should().BeFalse();
        vm.CustomFolders.Should().BeEmpty();
        vm.FolderStatusText.Should().Contain("Could not add folder");
        vm.CanEditFolders.Should().BeTrue();
    }

    private sealed class WaitingSync : ILibrarySyncService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null,
            CancellationToken cancellationToken = default, IProgress<SyncProgress>? progress = null)
        {
            progress?.Report(new(SyncStage.Scanning, "Scanning test provider...", 1, 4));
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The test scan must be cancelled.");
        }
    }

    private sealed class FailingSync : ILibrarySyncService
    {
        public Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null,
            CancellationToken cancellationToken = default, IProgress<SyncProgress>? progress = null) =>
            throw new IOException("disk failure");
    }

    private sealed class CompletedSync : ILibrarySyncService
    {
        public Task<SyncResult> SyncAsync(IEnumerable<string>? customLocalFolders = null,
            CancellationToken cancellationToken = default, IProgress<SyncProgress>? progress = null) =>
            Task.FromResult(new SyncResult(2, 1, new Dictionary<StoreKind, int>(), TimeSpan.FromSeconds(1))
            { ProviderErrors = new Dictionary<string, string> { ["Unavailable store"] = "Could not read manifests" } });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }
}
