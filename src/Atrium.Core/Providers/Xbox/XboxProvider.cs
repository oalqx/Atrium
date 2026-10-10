using System.Xml.Linq;
using Atrium.Core.Detection;
using Atrium.Core.Models;

namespace Atrium.Core.Providers.Xbox;

public sealed class XboxProvider : ILibraryProvider
{
    private readonly IReadOnlyList<string>? _customXboxFolders;

    public XboxProvider(IReadOnlyList<string>? customXboxFolders = null)
    {
        _customXboxFolders = customXboxFolders;
    }

    public StoreKind Store => StoreKind.Xbox;
    public string DisplayName => "Xbox / Microsoft Store";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var folders = _customXboxFolders ?? LauncherDetector.DetectXboxGamesFolders();
        if (folders.Count > 0)
        {
            return Task.FromResult(ProviderStatus.Available);
        }

        return Task.FromResult(ProviderStatus.Available);
    }

    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var folders = _customXboxFolders ?? LauncherDetector.DetectXboxGamesFolders();
        var results = new List<ProviderGame>();

        foreach (var rootFolder in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(rootFolder)) continue;

            var gameDirs = Directory.GetDirectories(rootFolder);
            foreach (var gameDir in gameDirs)
            {
                ct.ThrowIfCancellationRequested();
                var parsed = ParseXboxDirectory(gameDir);
                if (parsed != null)
                {
                    results.Add(parsed);
                }
            }
        }

        // Also query WinRT PackageManager if available
        try
        {
            var winRtGames = ScanWinRtPackages(ct);
            foreach (var winGame in winRtGames)
            {
                if (!results.Any(r => r.StoreId.Equals(winGame.StoreId, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(winGame);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }

        return await Task.FromResult(results);
    }

    private static ProviderGame? ParseXboxDirectory(string gameDir)
    {
        var contentDir = Path.Combine(gameDir, "Content");
        var baseDir = Directory.Exists(contentDir) ? contentDir : gameDir;

        var configFile = Path.Combine(baseDir, "MicrosoftGame.Config");
        if (!File.Exists(configFile))
        {
            configFile = Path.Combine(gameDir, "MicrosoftGame.Config");
            if (!File.Exists(configFile)) return null;
        }

        try
        {
            var xml = XDocument.Load(configFile);
            var gameElem = xml.Root;
            if (gameElem == null) return null;

            var identity = gameElem.Element("Identity");
            var packageName = identity?.Attribute("Name")?.Value;
            var titleId = gameElem.Element("TitleId")?.Value;
            var storeId = gameElem.Element("StoreId")?.Value;

            var shell = gameElem.Element("ShellVisuals");
            var displayName = shell?.Attribute("DefaultDisplayName")?.Value;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = Path.GetFileName(gameDir);
            }

            // Exclude helper/placeholder entries that aren't games
            if (displayName.Contains("GameSave", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Placeholder", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var exeElem = gameElem.Element("ExecutableList")?.Element("Executable");
            var exeName = exeElem?.Attribute("Name")?.Value;
            string? fullExePath = null;
            if (!string.IsNullOrEmpty(exeName))
            {
                var candidate = Path.Combine(baseDir, exeName);
                if (File.Exists(candidate))
                {
                    fullExePath = candidate;
                }
            }

            // Also check for gamelaunchhelper.exe
            var helperExe = Path.Combine(baseDir, "gamelaunchhelper.exe");
            if (File.Exists(helperExe))
            {
                fullExePath ??= helperExe;
            }

            // Artwork
            var storeLogo = shell?.Attribute("StoreLogo")?.Value;
            var splashLogo = shell?.Attribute("SplashScreenImage")?.Value;
            var largeLogo = shell?.Attribute("Square480x480Logo")?.Value ?? shell?.Attribute("Square150x150Logo")?.Value;

            string? localCover = null;
            string? localSplash = null;

            if (!string.IsNullOrEmpty(largeLogo) && File.Exists(Path.Combine(baseDir, largeLogo)))
                localCover = Path.Combine(baseDir, largeLogo);
            else if (!string.IsNullOrEmpty(storeLogo) && File.Exists(Path.Combine(baseDir, storeLogo)))
                localCover = Path.Combine(baseDir, storeLogo);

            if (!string.IsNullOrEmpty(splashLogo) && File.Exists(Path.Combine(baseDir, splashLogo)))
                localSplash = Path.Combine(baseDir, splashLogo);

            var id = !string.IsNullOrEmpty(titleId) ? titleId :
                     !string.IsNullOrEmpty(storeId) ? storeId :
                     !string.IsNullOrEmpty(packageName) ? packageName : Path.GetFileName(gameDir);

            // Determine launch command
            string? launchUri = null;
            if (File.Exists(helperExe))
            {
                launchUri = helperExe;
            }
            else if (!string.IsNullOrEmpty(fullExePath))
            {
                launchUri = fullExePath;
            }

            return new ProviderGame
            {
                Store = StoreKind.Xbox,
                StoreId = id,
                Title = displayName,
                SortTitle = CleanTitleForSorting(displayName),
                IsInstalled = true,
                InstallPath = gameDir,
                ExecutablePath = fullExePath,
                LaunchUri = launchUri,
                Artwork = new ArtworkSet
                {
                    LocalCoverPath = localCover,
                    LocalBackgroundPath = localSplash
                }
            };
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<ProviderGame> ScanWinRtPackages(CancellationToken ct)
    {
        var list = new List<ProviderGame>();
        try
        {
            var pkgManager = new Windows.Management.Deployment.PackageManager();
            var packages = pkgManager.FindPackagesForUser(string.Empty);

            foreach (var pkg in packages)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage) continue;

                    string installLoc;
                    try { installLoc = pkg.InstalledLocation?.Path ?? string.Empty; }
                    catch { continue; }

                    if (string.IsNullOrEmpty(installLoc) || !Directory.Exists(installLoc)) continue;

                    var config = Path.Combine(installLoc, "MicrosoftGame.Config");
                    if (File.Exists(config))
                    {
                        var displayName = pkg.DisplayName;
                        if (string.IsNullOrEmpty(displayName))
                        {
                            displayName = pkg.Id.Name;
                        }

                        var familyName = pkg.Id.FamilyName;
                        var launchUri = $"shell:AppsFolder\\{familyName}!App";

                        list.Add(new ProviderGame
                        {
                            Store = StoreKind.Xbox,
                            StoreId = pkg.Id.Name,
                            Title = displayName,
                            SortTitle = CleanTitleForSorting(displayName),
                            IsInstalled = true,
                            InstallPath = installLoc,
                            LaunchUri = launchUri
                        });
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }

        return list;
    }

    private static string CleanTitleForSorting(string title)
    {
        var t = title.Trim();
        if (t.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            return t[4..].Trim();
        }
        return t;
    }
}
