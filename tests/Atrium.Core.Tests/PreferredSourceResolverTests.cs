using Atrium.Core.Models;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class PreferredSourceResolverTests
{
    [Fact]
    public void Resolve_UsesGlobalPreference_WithInstalledFallbackAndGameOverride()
    {
        var game = new Game { Title = "Portal 2" };
        game.Sources.Add(new GameSource(StoreKind.Steam, "620", true));
        game.Sources.Add(new GameSource(StoreKind.Gog, "portal", true));
        var order = PreferredSourceResolver.CreateStoreOrder(StoreKind.Gog);
        order.Should().HaveCount(Enum.GetValues<StoreKind>().Length).And.OnlyHaveUniqueItems();
        PreferredSourceResolver.Resolve(game, order)!.Store.Should().Be(StoreKind.Gog);

        game.Sources[1] = game.Sources[1] with { IsInstalled = false };
        PreferredSourceResolver.Resolve(game, order)!.Store.Should().Be(StoreKind.Steam);
        game.User.PreferredStoreOverride = StoreKind.Gog;
        PreferredSourceResolver.Resolve(game, order)!.Store.Should().Be(StoreKind.Gog);
    }

    [Fact]
    public void Resolve_ReturnsSingleSource_WhenOnlyOneExists()
    {
        var game = new Game
        {
            Title = "Portal 2"
        };
        game.Sources.Add(new GameSource(StoreKind.Steam, "620", true));

        var resolved = PreferredSourceResolver.Resolve(game);

        resolved.Should().NotBeNull();
        resolved!.Store.Should().Be(StoreKind.Steam);
    }

    [Fact]
    public void Resolve_PrioritizesSteamByDefault_WhenMultipleStoresOwned()
    {
        var game = new Game
        {
            Title = "Hades"
        };
        game.Sources.Add(new GameSource(StoreKind.Epic, "hades_epic", true));
        game.Sources.Add(new GameSource(StoreKind.Steam, "1145360", true));

        var resolved = PreferredSourceResolver.Resolve(game);

        resolved.Should().NotBeNull();
        resolved!.Store.Should().Be(StoreKind.Steam);
    }

    [Fact]
    public void Resolve_RespectsUserOverride()
    {
        var game = new Game
        {
            Title = "Cyberpunk 2077",
            User = new UserGameData { PreferredStoreOverride = StoreKind.Gog }
        };
        game.Sources.Add(new GameSource(StoreKind.Steam, "1091500", true));
        game.Sources.Add(new GameSource(StoreKind.Gog, "gog_cyberpunk", true));

        var resolved = PreferredSourceResolver.Resolve(game);

        resolved.Should().NotBeNull();
        resolved!.Store.Should().Be(StoreKind.Gog);
    }

    [Fact]
    public void Resolve_PrioritizesInstalledOverUninstalled()
    {
        var game = new Game
        {
            Title = "Dead Cells"
        };
        // Steam owned but not installed, Epic installed
        game.Sources.Add(new GameSource(StoreKind.Steam, "588650", false));
        game.Sources.Add(new GameSource(StoreKind.Epic, "deadcells_epic", true));

        var resolved = PreferredSourceResolver.Resolve(game);

        resolved.Should().NotBeNull();
        resolved!.Store.Should().Be(StoreKind.Epic);
    }
}
