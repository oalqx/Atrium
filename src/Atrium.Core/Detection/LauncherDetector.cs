using Microsoft.Win32;

namespace Atrium.Core.Detection;

public sealed record LauncherPaths
{
    public string? SteamPath { get; init; }
    public string? EpicManifestsPath { get; init; }
    public string? GogGalaxyDatabasePath { get; init; }
    public IReadOnlyList<string> XboxGamesFolders { get; init; } = Array.Empty<string>();

    public bool HasSteam => !string.IsNullOrEmpty(SteamPath) && Directory.Exists(SteamPath);
    public bool HasEpic => !string.IsNullOrEmpty(EpicManifestsPath) && Directory.Exists(EpicManifestsPath);
    public bool HasGogGalaxy => !string.IsNullOrEmpty(GogGalaxyDatabasePath) && File.Exists(GogGalaxyDatabasePath);
    public bool HasXbox => XboxGamesFolders.Count > 0;
}

public static class LauncherDetector
{
    public static LauncherPaths Detect()
    {
        return new LauncherPaths
        {
            SteamPath = DetectSteamPath(),
            EpicManifestsPath = DetectEpicManifestsPath(),
            GogGalaxyDatabasePath = DetectGogGalaxyDatabasePath(),
            XboxGamesFolders = DetectXboxGamesFolders()
        };
    }

    public static string? DetectSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var val = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
            {
                return Path.GetFullPath(val);
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var val = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
            {
                return Path.GetFullPath(val);
            }
        }
        catch { }

        var defaults = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam"
        };

        return defaults.FirstOrDefault(Directory.Exists);
    }

    public static string? DetectEpicManifestsPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher");
            var appData = key?.GetValue("AppDataPath") as string;
            if (!string.IsNullOrEmpty(appData))
            {
                var manifests = Path.Combine(appData, "Manifests");
                if (Directory.Exists(manifests))
                {
                    return manifests;
                }
            }
        }
        catch { }

        var defaultPath = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    public static string? DetectGogGalaxyDatabasePath()
    {
        var defaultDb = @"C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db";
        return File.Exists(defaultDb) ? defaultDb : null;
    }

    public static IReadOnlyList<string> DetectXboxGamesFolders()
    {
        var list = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    var xboxFolder = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                    if (Directory.Exists(xboxFolder))
                    {
                        list.Add(xboxFolder);
                    }
                }
            }
        }
        catch { }

        return list;
    }
}
