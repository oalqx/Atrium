using Atrium.Core.Detection;
using Atrium.Core.Models;
using Atrium.Core.Providers.Gog;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class GogGalaxyProviderTests
{
    [Fact]
    public async Task GogGalaxyProvider_ImportsOwnedGames_FromGalaxyDatabase()
    {
        var dbPath = LauncherDetector.DetectGogGalaxyDatabasePath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return;

        var provider = new GogGalaxyProvider(dbPath);
        var status = await provider.GetStatusAsync();
        status.Should().Be(Atrium.Core.Providers.ProviderStatus.Available);

        var games = await provider.GetGamesAsync();
        games.Should().NotBeEmpty();

        // Should include games from Epic, GOG, and Xbox
        games.Should().Contain(g => g.Store == StoreKind.Epic);
        games.Should().Contain(g => g.Store == StoreKind.Gog);
        games.Should().Contain(g => g.Store == StoreKind.Xbox);
    }
}
