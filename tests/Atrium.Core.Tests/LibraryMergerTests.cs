using Atrium.Core.Library;
using Atrium.Core.Models;
using Atrium.Core.Providers;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class LibraryMergerTests
{
    [Fact]
    public void Merge_CombinesSameTitleAcrossStores_IntoSingleGame()
    {
        var steamGame = new ProviderGame
        {
            Store = StoreKind.Steam,
            StoreId = "1091500",
            Title = "Cyberpunk 2077",
            IsInstalled = true,
            LaunchUri = "steam://rungameid/1091500",
            PlaytimeMinutes = 320
        };

        var gogGame = new ProviderGame
        {
            Store = StoreKind.Gog,
            StoreId = "gog_cyberpunk",
            Title = "Cyberpunk 2077™",
            IsInstalled = false,
            PlaytimeMinutes = 100
        };

        var merged = LibraryMerger.Merge(new[] { steamGame, gogGame });

        merged.Should().HaveCount(1);
        var game = merged[0];
        game.Title.Should().Be("Cyberpunk 2077");
        game.Sources.Should().HaveCount(2);
        game.IsInstalled.Should().BeTrue();
        game.TotalPlaytimeMinutes.Should().Be(420);
    }

    [Fact]
    public void Merge_LinksReleasesViaCrossStoreKeys()
    {
        var steamGame = new ProviderGame
        {
            Store = StoreKind.Steam,
            StoreId = "17410",
            Title = "Mirror's Edge",
            IsInstalled = true
        };

        var xboxGame = new ProviderGame
        {
            Store = StoreKind.Xbox,
            StoreId = "810085329",
            Title = "Mirrors Edge (Xbox)",
            CrossStoreReleaseKeys = new[] { "steam_17410" },
            IsInstalled = false
        };

        var merged = LibraryMerger.Merge(new[] { steamGame, xboxGame });

        merged.Should().HaveCount(1);
        var game = merged[0];
        game.Sources.Should().HaveCount(2);
    }
}
