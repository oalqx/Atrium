using Atrium.Core.Models;
using Microsoft.Win32;

namespace Atrium.Core.Providers.Gog;

public sealed class GogProvider : ILibraryProvider
{
    public StoreKind Store => StoreKind.Gog;
    public string DisplayName => "GOG";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ProviderStatus.Available);
    }

    public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<ProviderGame>();

        try
        {
            using var rootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (rootKey != null)
            {
                foreach (var subKeyName in rootKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var gameKey = rootKey.OpenSubKey(subKeyName);
                    if (gameKey == null) continue;

                    var gameId = (gameKey.GetValue("gameID") as string) ?? subKeyName;
                    var gameName = gameKey.GetValue("gameName") as string;
                    var installPath = gameKey.GetValue("path") as string;
                    var exeName = gameKey.GetValue("exe") as string;

                    if (string.IsNullOrEmpty(gameName)) continue;

                    string? exePath = null;
                    if (!string.IsNullOrEmpty(installPath) && !string.IsNullOrEmpty(exeName))
                    {
                        var candidate = Path.Combine(installPath, exeName);
                        if (File.Exists(candidate)) exePath = candidate;
                    }

                    results.Add(new ProviderGame
                    {
                        Store = StoreKind.Gog,
                        StoreId = gameId,
                        Title = gameName,
                        SortTitle = CleanTitleForSorting(gameName),
                        IsInstalled = true,
                        InstallPath = installPath,
                        ExecutablePath = exePath,
                        LaunchUri = $"goggalaxy://openGameView/{gameId}"
                    });
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }

        return Task.FromResult<IReadOnlyList<ProviderGame>>(results);
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
