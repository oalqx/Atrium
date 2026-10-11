using Atrium.App.ViewModels;
using Atrium.Core.Models;
using Atrium.Core.Storage;
using Atrium.Core.System;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Atrium.App.Tests;

public class ViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"atrium_vm_{Guid.NewGuid():N}");
    private readonly string _registryPath = @"Software\AtriumTests\" + Guid.NewGuid().ToString("N");

    public ViewModelTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task FavoriteAndHiddenChanges_ArePersistedAndCanBeRestored()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var game = new Game { Title = "Portal 2" };
        await db.UpsertGamesAsync([game]);
        var card = new GameCardViewModel(game, db);

        await card.ToggleFavoriteAsync();
        await card.ToggleHiddenAsync();

        var saved = (await db.GetAllGamesAsync()).Single();
        saved.User.Favorite.Should().BeTrue();
        saved.User.Hidden.Should().BeTrue();
        card.IsHidden.Should().BeTrue();
        card.HideActionLabel.Should().Be("Restore to library");
        await card.ToggleHiddenAsync();
        (await db.GetAllGamesAsync()).Single().User.Hidden.Should().BeFalse();
    }

    [Fact]
    public async Task DraftLauncherChoice_IsSavedExplicitly_AndCanReturnToGlobalPreference()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var game = new Game { Title = "Portal" };
        game.Sources.Add(new GameSource(StoreKind.Steam, "400", true));
        game.Sources.Add(new GameSource(StoreKind.Gog, "portal", true));
        await db.UpsertGamesAsync([game]);
        var launcher = new RecordingLauncher();
        var card = new GameCardViewModel(game, db, launcher: launcher);
        card.PendingStorePreference = card.StorePreferences.Single(p => p.Store == StoreKind.Gog);
        card.PrimarySource!.Store.Should().Be(StoreKind.Steam);
        (await db.GetAllGamesAsync()).Single().User.PreferredStoreOverride.Should().BeNull();
        card.SavePreferredStoreCommand.CanExecute(null).Should().BeTrue();

        await card.SavePreferredStoreAsync();
        card.Launch();

        launcher.Source!.Store.Should().Be(StoreKind.Gog);
        card.ActionMessage.Should().Be("Launcher failed for this test");
        (await db.GetAllGamesAsync()).Single().User.PreferredStoreOverride.Should().Be(StoreKind.Gog);
        card.LaunchSource(game.Sources[0]);
        launcher.Source!.Store.Should().Be(StoreKind.Steam);
        card.PrimarySource!.Store.Should().Be(StoreKind.Gog);
        (await db.GetAllGamesAsync()).Single().User.PreferredStoreOverride.Should().Be(StoreKind.Gog);
        card.SavePreferredStoreCommand.CanExecute(null).Should().BeFalse();
        card.UpdateStoreOrder(PreferredSourceResolver.CreateStoreOrder(StoreKind.Steam));
        card.PrimarySource!.Store.Should().Be(StoreKind.Gog);
        card.PendingStorePreference = card.StorePreferences[0];
        await card.SavePreferredStoreAsync();
        card.PrimarySource!.Store.Should().Be(StoreKind.Steam);
        (await db.GetAllGamesAsync()).Single().User.PreferredStoreOverride.Should().BeNull();
    }

    [Fact]
    public async Task SavingAStaleCard_ReportsFailureAndKeepsPreviousState()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var game = new Game { Title = "Removed game" };
        game.Sources.Add(new GameSource(StoreKind.Steam, "400", true));
        var card = new GameCardViewModel(game, db);

        await card.ToggleFavoriteAsync();
        card.IsFavorite.Should().BeFalse();
        game.User.Favorite.Should().BeFalse();
        card.ActionMessage.Should().Contain("Could not save favorite");
        await card.ToggleHiddenAsync();
        card.IsHidden.Should().BeFalse();
        card.ActionMessage.Should().Contain("Could not save visibility");
        card.PendingStorePreference = card.StorePreferences[1];
        await card.SavePreferredStoreAsync();
        card.SelectedStorePreference.Store.Should().BeNull();
        card.PendingStorePreference!.Store.Should().BeNull();
        card.ActionMessage.Should().Contain("Could not save launcher preference");
        card.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void LaunchWithoutSources_ShowsActionableFeedback()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        var card = new GameCardViewModel(new Game { Title = "Unlinked" }, db);
        card.Launch();
        card.ActionMessage.Should().Contain("No launcher is linked");
        card.HasActionMessage.Should().BeTrue();
    }

    private sealed class RecordingLauncher : IGameLaunchService
    {
        public GameSource? Source { get; private set; }
        public GameLaunchResult Launch(GameSource source)
        {
            Source = source;
            return new(false, "Launcher failed for this test");
        }
    }

    [Fact]
    public void MissingOrFailedCover_DisplaysTheTitleFallback()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        var game = new Game { Title = "No cover" };
        var card = new GameCardViewModel(game, db);
        card.CoverUri.Should().BeNull();
        card.HasCover.Should().BeFalse();

        game.Artwork = new ArtworkSet { CoverUrl = "https://example.com/cover.jpg" };
        card.HasCover.Should().BeTrue();
        card.CoverLoadFailed = true;
        card.HasCover.Should().BeFalse();
    }

    [Fact]
    public void ChangingGlobalPreference_UpdatesTheCardAndLaunchSource()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        var game = new Game { Title = "Portal 2" };
        game.Sources.Add(new GameSource(StoreKind.Steam, "620", true));
        game.Sources.Add(new GameSource(StoreKind.Gog, "portal", true));
        var card = new GameCardViewModel(game, db, PreferredSourceResolver.CreateStoreOrder(StoreKind.Steam));
        var notifications = new List<string?>();
        card.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        card.UpdateStoreOrder(PreferredSourceResolver.CreateStoreOrder(StoreKind.Gog));

        card.PrimarySource!.Store.Should().Be(StoreKind.Gog);
        card.PrimaryStoreName.Should().Be("GOG");
        notifications.Should().Contain(nameof(GameCardViewModel.PrimaryStoreName));
    }

    [Fact]
    public async Task RevertAll_RestoresAnUninstalledGame_AndPreservesExternalPreferences()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var managedExe = Path.Combine(_tempDir, "managed.exe");
        var externalExe = Path.Combine(_tempDir, "external.exe");
        File.WriteAllText(managedExe, "dummy");
        File.WriteAllText(externalExe, "dummy");
        using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
        key.SetValue(managedExe, "GpuPreference=1;");
        key.SetValue(externalExe, "GpuPreference=2;");
        var gpu = new GpuPreferenceService(db, _registryPath);
        await gpu.SetHighPerformanceAsync(managedExe);
        File.Delete(managedExe);
        var game = new Game { Title = "External" };
        game.Sources.Add(new GameSource(StoreKind.Local, "external", true, ExecutablePath: externalExe));
        await db.UpsertGamesAsync([game]);
        var vm = new GpuOptimizerViewModel(db, gpu);
        await vm.LoadInstalledGamesAsync();
        vm.InstalledGameItems.Should().ContainSingle();
        vm.InstalledGameItems[0].CanChangePreference.Should().BeFalse();

        await vm.RevertAllAsync();

        key.GetValue(managedExe).Should().Be("GpuPreference=1;");
        key.GetValue(externalExe).Should().Be("GpuPreference=2;");
        (await gpu.GetModifiedExePathsAsync()).Should().BeEmpty();
        vm.IsOptimizing.Should().BeFalse();
    }

    [Fact]
    public async Task MissingExecutable_IsNotReportedAsOptimized()
    {
        using var db = new LibraryDatabase(Path.Combine(_tempDir, "library.db"));
        await db.InitializeAsync();
        var gpu = new GpuPreferenceService(db, _registryPath);
        var item = new GameGpuItemViewModel("Missing", Path.Combine(_tempDir, "missing.exe"), StoreKind.Local, false, gpu);

        await item.ToggleGpuPreferenceAsync();

        item.IsHighPerformance.Should().BeFalse();
        item.IsManagedByAtrium.Should().BeFalse();
        item.IsChangingPreference.Should().BeFalse();
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_registryPath, false);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, true);
    }
}
