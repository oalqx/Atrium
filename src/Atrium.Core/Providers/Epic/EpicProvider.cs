using System.Text.Json;
using System.Text.Json.Serialization;
using Atrium.Core.Detection;
using Atrium.Core.Models;

namespace Atrium.Core.Providers.Epic;

public sealed class EpicProvider : ILibraryProvider
{
    private readonly string? _customManifestsPath;

    public EpicProvider(string? customManifestsPath = null)
    {
        _customManifestsPath = customManifestsPath;
    }

    public StoreKind Store => StoreKind.Epic;
    public string DisplayName => "Epic Games";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var path = _customManifestsPath ?? LauncherDetector.DetectEpicManifestsPath();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return Task.FromResult(ProviderStatus.NotInstalled);
        }

        return Task.FromResult(ProviderStatus.Available);
    }

    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var manifestsPath = _customManifestsPath ?? LauncherDetector.DetectEpicManifestsPath();
        if (string.IsNullOrEmpty(manifestsPath) || !Directory.Exists(manifestsPath))
        {
            return Array.Empty<ProviderGame>();
        }

        var results = new List<ProviderGame>();
        var itemFiles = Directory.GetFiles(manifestsPath, "*.item");

        foreach (var itemPath in itemFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(itemPath, ct);
                var item = JsonSerializer.Deserialize<EpicItemManifest>(json);
                if (item == null || item.IsIncompleteInstall || string.IsNullOrEmpty(item.AppName) || string.IsNullOrEmpty(item.DisplayName))
                {
                    continue;
                }

                string? exePath = null;
                if (!string.IsNullOrEmpty(item.InstallLocation) && !string.IsNullOrEmpty(item.LaunchExecutable))
                {
                    var fullExe = Path.Combine(item.InstallLocation, item.LaunchExecutable);
                    if (File.Exists(fullExe))
                    {
                        exePath = fullExe;
                    }
                }

                // Check for local cover artwork in install folder or .egstore
                var artwork = DiscoverArtwork(item.InstallLocation);

                // Build launch URI
                string? launchUri = null;
                if (!string.IsNullOrEmpty(item.CatalogNamespace) && !string.IsNullOrEmpty(item.CatalogItemId) && !string.IsNullOrEmpty(item.AppName))
                {
                    launchUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(item.CatalogNamespace)}%3A{Uri.EscapeDataString(item.CatalogItemId)}%3A{Uri.EscapeDataString(item.AppName)}?action=launch&silent=true";
                }
                else if (!string.IsNullOrEmpty(item.AppName))
                {
                    launchUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(item.AppName)}?action=launch&silent=true";
                }

                results.Add(new ProviderGame
                {
                    Store = StoreKind.Epic,
                    StoreId = item.AppName,
                    Title = item.DisplayName,
                    SortTitle = CleanTitleForSorting(item.DisplayName),
                    IsInstalled = true,
                    InstallPath = item.InstallLocation,
                    ExecutablePath = exePath,
                    LaunchUri = launchUri,
                    Artwork = artwork
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                // Continue on individual malformed manifest
            }
        }

        return results;
    }

    private static ArtworkSet DiscoverArtwork(string? installLocation)
    {
        if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
        {
            return new ArtworkSet();
        }

        string? cover = null;
        string? icon = null;

        try
        {
            // Look for standard icon files in the install folder
            var icoFiles = Directory.GetFiles(installLocation, "*.ico", SearchOption.TopDirectoryOnly);
            icon = icoFiles.FirstOrDefault();

            // Look for splash or cover images
            var imageFiles = Directory.GetFiles(installLocation, "*.png", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(installLocation, "*.jpg", SearchOption.TopDirectoryOnly))
                .ToList();

            cover = imageFiles.FirstOrDefault(f =>
                f.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("splash", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("poster", StringComparison.OrdinalIgnoreCase));
        }
        catch { }

        return new ArtworkSet
        {
            LocalCoverPath = cover,
            LocalIconPath = icon
        };
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

    private sealed class EpicItemManifest
    {
        [JsonPropertyName("DisplayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("AppName")]
        public string? AppName { get; set; }

        [JsonPropertyName("CatalogNamespace")]
        public string? CatalogNamespace { get; set; }

        [JsonPropertyName("CatalogItemId")]
        public string? CatalogItemId { get; set; }

        [JsonPropertyName("InstallLocation")]
        public string? InstallLocation { get; set; }

        [JsonPropertyName("LaunchExecutable")]
        public string? LaunchExecutable { get; set; }

        [JsonPropertyName("bIsIncompleteInstall")]
        public bool IsIncompleteInstall { get; set; }
    }
}
