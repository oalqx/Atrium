using Atrium.Core.Models;
using Atrium.Core.System;
using FluentAssertions;
using Xunit;

namespace Atrium.Core.Tests;

public class GameLaunchServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("game.exe")]
    [InlineData("not a URI")]
    public void MissingOrRelativeLaunchTarget_ReturnsFailureWithoutOpeningAnything(string? target)
    {
        var result = new GameLaunchService().Launch(new GameSource(StoreKind.Local, "test", true, LaunchUri: target));
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Sync your library");
    }

    [Fact]
    public void MissingLocalExecutable_ReturnsFailureInsteadOfOpeningAShellDialog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-atrium-{Guid.NewGuid():N}.exe");
        var result = new GameLaunchService().Launch(new GameSource(StoreKind.Local, "test", true, LaunchUri: path));
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("executable could not be found");
    }
}
