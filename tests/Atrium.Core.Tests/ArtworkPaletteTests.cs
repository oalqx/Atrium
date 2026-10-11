using Atrium.Core.Models;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class ArtworkPaletteTests
{
    [Fact]
    public void Palette_ReflectsTheArtwork_AndKeepsButtonTextReadable()
    {
        var warm = ArtworkPalette.FromBgra([30, 40, 180, 255, 35, 45, 190, 255]);
        var cool = ArtworkPalette.FromBgra([210, 80, 30, 255, 200, 85, 25, 255]);
        warm.Accent.R.Should().BeGreaterThan(warm.Accent.B);
        cool.Accent.B.Should().BeGreaterThan(cool.Accent.R);
        warm.Surface.Should().NotBe(cool.Surface);
        foreach (var palette in new[] { warm, cool, ArtworkPalette.Default })
        {
            var contrast = (Math.Max(palette.Accent.Luminance, palette.ButtonText.Luminance) + 0.05) /
                (Math.Min(palette.Accent.Luminance, palette.ButtonText.Luminance) + 0.05);
            contrast.Should().BeGreaterThanOrEqualTo(4.5);
            palette.Surface.Luminance.Should().BeLessThan(0.02);
        }
    }

    [Fact]
    public void Palette_IgnoresTransparentAndNeutralBorders()
    {
        var pixels = Enumerable.Range(0, 100).SelectMany(_ => new byte[] { 255, 255, 255, 255 }).ToList();
        pixels.AddRange([0, 0, 255, 0, 10, 180, 30, 255]);
        var palette = ArtworkPalette.FromBgra(pixels.ToArray());
        palette.Accent.G.Should().BeGreaterThan(palette.Accent.R);
        palette.Accent.G.Should().BeGreaterThan(palette.Accent.B);
        ArtworkPalette.FromBgra([0, 0, 0, 255, 255, 255, 255, 255]).Should().Be(ArtworkPalette.Default);
        ArtworkPalette.FromBgra([]).Should().Be(ArtworkPalette.Default);
    }
}
