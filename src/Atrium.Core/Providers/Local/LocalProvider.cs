using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Atrium.Core.Models;

namespace Atrium.Core.Providers.Local;

public sealed class LocalProvider : ILibraryProvider
{
    private readonly IReadOnlyList<string> _watchPaths;

    public LocalProvider(IReadOnlyList<string>? watchPaths = null)
    {
        _watchPaths = watchPaths ?? Array.Empty<string>();
    }

    public StoreKind Store => StoreKind.Local;
    public string DisplayName => "Local Games";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ProviderStatus.Available);
    }

    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default)
    {
        var results = new List<ProviderGame>();
        var seenExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ct.ThrowIfCancellationRequested();

        foreach (var path in _watchPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var game = seenExecutables.Add(Path.GetFullPath(path)) ? CreateFromExe(path) : null;
                if (game != null) results.Add(game);
            }
            else if (Directory.Exists(path))
            {
                ScanDirectory(path, results, seenExecutables, ct);
            }
        }

        return await Task.FromResult(results);
    }

    private static void ScanDirectory(string rootDir, List<ProviderGame> results, HashSet<string> seenExecutables, CancellationToken ct)
    {
        try
        {
            var exes = Directory.EnumerateFiles(rootDir, "*.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive
            });
            foreach (var exe in exes)
            {
                ct.ThrowIfCancellationRequested();
                var filename = Path.GetFileName(exe);
                if (IsExcluded(filename) || !seenExecutables.Add(Path.GetFullPath(exe))) continue;

                var game = CreateFromExe(exe);
                if (game != null)
                {
                    results.Add(game);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ProviderGame? CreateFromExe(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var title = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName :
                        !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription :
                        Path.GetFileNameWithoutExtension(exePath);

            var normalizedPath = Path.GetFullPath(exePath).ToLowerInvariant();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
            var storeId = Convert.ToHexString(hashBytes)[..16];

            return new ProviderGame
            {
                Store = StoreKind.Local,
                StoreId = storeId,
                Title = title.Trim(),
                SortTitle = CleanTitleForSorting(title),
                IsInstalled = true,
                InstallPath = Path.GetDirectoryName(exePath),
                ExecutablePath = exePath,
                LaunchUri = exePath
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExcluded(string filename)
    {
        var lower = filename.ToLowerInvariant();
        return lower.Contains("unins") ||
               lower.Contains("setup") ||
               lower.Contains("install") ||
               lower.Contains("update") ||
               lower.Contains("patch") ||
               lower.Contains("crash") ||
               lower.Contains("redist");
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
