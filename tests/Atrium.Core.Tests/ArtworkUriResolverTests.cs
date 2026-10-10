using Atrium.Core.Models;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class ArtworkUriResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not a URI")]
    [InlineData("ftp://example.com/image.jpg")]
    public void InvalidOrMissingArtwork_ReturnsNoUri(string? location) =>
        ArtworkUriResolver.Resolve(location).Should().BeNull();

    [Fact]
    public void LocalCover_WithSpacesAndRepeatedSeparators_BecomesAnEscapedFileUri()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atrium cover {Guid.NewGuid():N}.jpg");
        File.WriteAllText(path, "dummy");
        try
        {
            var uri = ArtworkUriResolver.Resolve(path.Replace("\\", "\\\\"));
            uri.Should().NotBeNull();
            uri!.IsFile.Should().BeTrue();
            uri.AbsoluteUri.Should().StartWith("file:///").And.Contain("%20");
            uri.LocalPath.Should().Be(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemoteCover_RetainsItsHttpsUri() =>
        ArtworkUriResolver.Resolve("https://example.com/cover.jpg")!.AbsoluteUri
            .Should().Be("https://example.com/cover.jpg");
}
