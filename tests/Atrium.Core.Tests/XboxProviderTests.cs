using Atrium.Core.Detection;
using Atrium.Core.Models;
using Atrium.Core.Providers.Xbox;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class XboxProviderTests
{
    [Fact]
    public async Task XboxProvider_DetectsInstalledXboxGames_OnCurrentMachine()
    {
        var folders = LauncherDetector.DetectXboxGamesFolders();
        if (folders.Count == 0) return;

        var provider = new XboxProvider(folders);
        var games = await provider.GetGamesAsync();

        // Minecraft should be discovered on this machine
        games.Should().NotBeEmpty();
        games.Should().Contain(g => g.Title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase));
        games.Should().AllSatisfy(g =>
        {
            g.Store.Should().Be(StoreKind.Xbox);
            g.IsInstalled.Should().BeTrue();
            g.Title.Should().NotBeNullOrWhiteSpace();
        });
    }
}
