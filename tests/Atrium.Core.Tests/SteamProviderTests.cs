using Atrium.Core.Detection;
using Atrium.Core.Models;
using Atrium.Core.Providers;
using Atrium.Core.Providers.Steam;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class SteamProviderTests
{
    [Fact]
    public async Task SteamProvider_DetectsInstalledSteamGames_OnCurrentMachine()
    {
        var steamPath = LauncherDetector.DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            // If Steam is not installed in the test environment, skip gracefully
            return;
        }

        var provider = new SteamProvider(steamPath);
        var status = await provider.GetStatusAsync();
        status.Should().Be(ProviderStatus.Available);

        var games = await provider.GetGamesAsync();
        games.Should().NotBeEmpty();

        // Verify none of the games are redistributables
        games.Should().NotContain(g => g.StoreId == "228980");
        games.Should().AllSatisfy(g =>
        {
            g.Store.Should().Be(StoreKind.Steam);
            g.LaunchUri.Should().StartWith("steam://rungameid/");
            g.Title.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void DiscoverLocalArtwork_FindsCoverWhenAvailable()
    {
        var steamPath = LauncherDetector.DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath)) return;

        // Test with a known app id or dummy
        var art = SteamProvider.DiscoverLocalArtwork(steamPath, "440"); // TF2
        art.Should().NotBeNull();
        art.CoverUrl.Should().Contain("440/library_600x900.jpg");
    }
}
