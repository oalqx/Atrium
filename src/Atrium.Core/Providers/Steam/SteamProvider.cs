using Atrium.Core.Detection;
using Atrium.Core.Models;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;

namespace Atrium.Core.Providers.Steam;

public sealed class SteamProvider : ILibraryProvider
{
    private static readonly HashSet<string> KnownToolAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "228980", // Steamworks Common Redistributables
        "228989", // Steamworks Shared
        "228990",
        "1391110", // Steam Linux Runtime
        "1070560", // Steam Linux Runtime - Soldier
        "1628350", // Steam Linux Runtime - Sniper
        "896660",  // SteamVR
        "250820",  // SteamVR
    };

    private readonly string? _customSteamPath;

    public SteamProvider(string? steamPath = null)
    {
        _customSteamPath = steamPath;
    }

    public StoreKind Store => StoreKind.Steam;
    public string DisplayName => "Steam";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var path = _customSteamPath ?? LauncherDetector.DetectSteamPath();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return Task.FromResult(ProviderStatus.NotInstalled);
        }

        return Task.FromResult(ProviderStatus.Available);
    }

    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var steamPath = _customSteamPath ?? LauncherDetector.DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            return Array.Empty<ProviderGame>();
        }

        var results = new List<ProviderGame>();
        var libraryFolders = GetLibraryFolders(steamPath);

        // Load local playtime cache if available
        var localPlaytime = GetLocalPlaytimes(steamPath);

        foreach (var libraryPath in libraryFolders)
        {
            ct.ThrowIfCancellationRequested();
            var steamapps = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                continue;
            }

            var manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf");
            foreach (var manifestPath in manifests)
            {
                ct.ThrowIfCancellationRequested();
                var game = ParseManifest(manifestPath, steamPath, libraryPath, localPlaytime);
                if (game != null && !game.IsToolOrRedistributable)
                {
                    results.Add(game);
                }
            }
        }

        return await Task.FromResult(results);
    }

    public static IReadOnlyList<string> GetLibraryFolders(string steamPath)
    {
        var list = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return list;
        }

        try
        {
            var content = File.ReadAllText(vdfPath);
            VProperty vol = VdfConvert.Deserialize(content);
            var obj = vol.Value as VObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value is VObject folderObj && folderObj["path"] != null)
                    {
                        var p = folderObj["path"]!.ToString();
                        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                        {
                            var normalized = Path.GetFullPath(p);
                            if (!list.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(normalized);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return list;
    }

    private static ProviderGame? ParseManifest(
        string manifestPath,
        string steamPath,
        string libraryPath,
        IReadOnlyDictionary<string, (int Playtime, DateTimeOffset? LastPlayed)> playtimes)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);
            VProperty parsed = VdfConvert.Deserialize(content);
            var appState = parsed.Value as VObject;
            if (appState == null)
            {
                return null;
            }

            var appId = appState["appid"]?.ToString();
            var name = appState["name"]?.ToString();
            var installDir = appState["installdir"]?.ToString();

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Exclude known redistributables and tools
            var isTool = KnownToolAppIds.Contains(appId) ||
                         name.EndsWith("Dedicated Server", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Steamworks", StringComparison.OrdinalIgnoreCase);

            string? installPath = null;
            string? exePath = null;

            if (!string.IsNullOrEmpty(installDir))
            {
                var fullCommonPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
                if (Directory.Exists(fullCommonPath))
                {
                    installPath = fullCommonPath;
                    exePath = DetectMainExecutable(fullCommonPath);
                }
            }

            // Look up local artwork cache
            var artwork = DiscoverLocalArtwork(steamPath, appId);

            // Playtime
            int? playtimeMinutes = null;
            DateTimeOffset? lastPlayed = null;

            if (playtimes.TryGetValue(appId, out var stat))
            {
                playtimeMinutes = stat.Playtime;
                lastPlayed = stat.LastPlayed;
            }

            // Also check LastOwner / last played attributes in manifest
            if (appState["LastPlayed"] != null && long.TryParse(appState["LastPlayed"]!.ToString(), out var lp) && lp > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(lp);
                if (!lastPlayed.HasValue || dt > lastPlayed.Value)
                {
                    lastPlayed = dt;
                }
            }

            return new ProviderGame
            {
                Store = StoreKind.Steam,
                StoreId = appId,
                Title = name,
                SortTitle = CleanTitleForSorting(name),
                IsInstalled = true,
                InstallPath = installPath,
                ExecutablePath = exePath,
                LaunchUri = $"steam://rungameid/{appId}",
                PlaytimeMinutes = playtimeMinutes,
                LastPlayed = lastPlayed,
                Artwork = artwork,
                IsToolOrRedistributable = isTool
            };
        }
        catch
        {
            return null;
        }
    }

    public static ArtworkSet DiscoverLocalArtwork(string steamPath, string appId)
    {
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache", appId);
        string? cover = null;
        string? hero = null;
        string? logo = null;

        if (Directory.Exists(cacheDir))
        {
            var pCover = Path.Combine(cacheDir, "library_600x900.jpg");
            if (File.Exists(pCover)) cover = pCover;

            var pHero = Path.Combine(cacheDir, "library_hero.jpg");
            if (File.Exists(pHero)) hero = pHero;

            var pLogo = Path.Combine(cacheDir, "logo.png");
            if (File.Exists(pLogo)) logo = pLogo;

            var pHeader = Path.Combine(cacheDir, "header.jpg");
            if (cover == null && File.Exists(pHeader)) cover = pHeader;
        }

        // Standard Steam CDN URLs as fallback
        var cdnCover = $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";
        var cdnHero = $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg";
        var cdnLogo = $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/logo.png";
        var cdnIcon = $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_231x87.jpg";

        return new ArtworkSet
        {
            LocalCoverPath = cover,
            CoverUrl = cdnCover,
            LocalBackgroundPath = hero,
            BackgroundUrl = cdnHero,
            LocalLogoPath = logo,
            LogoUrl = cdnLogo,
            IconUrl = cdnIcon
        };
    }

    private static IReadOnlyDictionary<string, (int Playtime, DateTimeOffset? LastPlayed)> GetLocalPlaytimes(string steamPath)
    {
        var dict = new Dictionary<string, (int Playtime, DateTimeOffset? LastPlayed)>(StringComparer.OrdinalIgnoreCase);
        var userData = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userData))
        {
            return dict;
        }

        foreach (var userDir in Directory.GetDirectories(userData))
        {
            var localConfig = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(localConfig)) continue;

            try
            {
                var content = File.ReadAllText(localConfig);
                VProperty parsed = VdfConvert.Deserialize(content);
                var obj = parsed.Value as VObject;
                var software = obj?["Software"] as VObject;
                var valve = software?["Valve"] as VObject;
                var steam = valve?["Steam"] as VObject;
                var apps = steam?["Apps"] as VObject;

                if (apps != null)
                {
                    foreach (var appProp in apps.Properties())
                    {
                        var appId = appProp.Key;
                        if (appProp.Value is VObject appData)
                        {
                            int mins = 0;
                            DateTimeOffset? last = null;

                            if (appData["Playtime"] != null && int.TryParse(appData["Playtime"]!.ToString(), out var p))
                            {
                                mins = p;
                            }

                            if (appData["LastPlayed"] != null && long.TryParse(appData["LastPlayed"]!.ToString(), out var lp) && lp > 0)
                            {
                                last = DateTimeOffset.FromUnixTimeSeconds(lp);
                            }

                            if (mins > 0 || last.HasValue)
                            {
                                dict[appId] = (mins, last);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return dict;
    }

    public static string? DetectMainExecutable(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return null;

        try
        {
            var exeFiles = Directory.GetFiles(directoryPath, "*.exe", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => !IsExcludedExe(f.Name))
                .OrderByDescending(f => f.Length)
                .ToList();

            return exeFiles.FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExcludedExe(string filename)
    {
        var lower = filename.ToLowerInvariant();
        return lower.Contains("crash") ||
               lower.Contains("report") ||
               lower.Contains("redist") ||
               lower.Contains("setup") ||
               lower.Contains("install") ||
               lower.Contains("unins") ||
               lower.Contains("dxwebsetup") ||
               lower.Contains("vcredist") ||
               lower.Contains("unitycrashhandler") ||
               lower.Contains("easyanticheat") ||
               lower.Contains("battleye");
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
