using System.Diagnostics;
using Atrium.Core.Models;

namespace Atrium.Core.System;

public sealed record GameLaunchResult(bool Success, string Message);

public interface IGameLaunchService
{
    GameLaunchResult Launch(GameSource source);
}

public sealed class GameLaunchService : IGameLaunchService
{
    public GameLaunchResult Launch(GameSource source)
    {
        var target = string.IsNullOrWhiteSpace(source.LaunchUri) ? source.ExecutablePath : source.LaunchUri;
        if (string.IsNullOrWhiteSpace(target))
            return new(false, "No launch target is available. Sync your library or choose another launcher.");

        try
        {
            string? workingDirectory = null;
            if (Path.IsPathFullyQualified(target))
            {
                target = Path.GetFullPath(target);
                if (!File.Exists(target))
                    return new(false, "The game executable could not be found. Sync your library to refresh its location.");
                workingDirectory = Path.GetDirectoryName(target);
            }
            else if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.IsFile)
            {
                return new(false, "The launch target is invalid. Sync your library or choose another launcher.");
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = true
            });
            return new(true, "Launch request sent. Your launcher may need a moment to open.");
        }
        catch (Exception ex)
        {
            return new(false, $"Could not open this game: {ex.Message}");
        }
    }
}
