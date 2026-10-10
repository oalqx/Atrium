using Atrium.Core.Detection;
using Atrium.Core.Models;
using Atrium.Core.Providers;
using Atrium.Core.Providers.Epic;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class EpicProviderTests
{
    [Fact]
    public async Task EpicProvider_DetectsInstalledEpicGames_OnCurrentMachine()
    {
        var manifests = LauncherDetector.DetectEpicManifestsPath();
        if (string.IsNullOrEmpty(manifests) || !Directory.Exists(manifests))
        {
            return;
        }

        var provider = new EpicProvider(manifests);
        var status = await provider.GetStatusAsync();
        status.Should().Be(ProviderStatus.Available);

        var games = await provider.GetGamesAsync();
        games.Should().NotBeEmpty();

        games.Should().AllSatisfy(g =>
        {
            g.Store.Should().Be(StoreKind.Epic);
            g.Title.Should().NotBeNullOrWhiteSpace();
            g.LaunchUri.Should().StartWith("com.epicgames.launcher://apps/");
            g.IsInstalled.Should().BeTrue();
        });
    }
}
