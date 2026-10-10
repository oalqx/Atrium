using System.Text.RegularExpressions;
using Atrium.Core.Models;
using Atrium.Core.Providers;

namespace Atrium.Core.Library;

public static class LibraryMerger
{
    public static IReadOnlyList<Game> Merge(IEnumerable<ProviderGame> providerGames)
    {
        var gamesList = providerGames.ToList();
        if (gamesList.Count == 0) return Array.Empty<Game>();

        int n = gamesList.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int i)
        {
            while (i != parent[i])
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb)
            {
                parent[ra] = rb;
            }
        }

        // 1. Map (Store, StoreId) to index
        var storeIndexMap = new Dictionary<(StoreKind Store, string StoreId), int>();
        for (int i = 0; i < n; i++)
        {
            storeIndexMap[(gamesList[i].Store, gamesList[i].StoreId)] = i;
        }

        // 2. Link by cross-store keys (from Galaxy DB)
        for (int i = 0; i < n; i++)
        {
            var pg = gamesList[i];
            foreach (var link in pg.CrossStoreReleaseKeys)
            {
                var parsedLink = ParseReleaseKey(link);
                if (parsedLink.HasValue && storeIndexMap.TryGetValue(parsedLink.Value, out var targetIdx))
                {
                    Union(i, targetIdx);
                }
            }
        }

        // 3. Link by normalized title (only if from different stores)
        var titleMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            var norm = NormalizeTitle(gamesList[i].Title);
            if (string.IsNullOrEmpty(norm)) continue;

            if (!titleMap.TryGetValue(norm, out var list))
            {
                list = new List<int>();
                titleMap[norm] = list;
            }
            list.Add(i);
        }

        foreach (var group in titleMap.Values)
        {
            if (group.Count > 1)
            {
                // Verify store uniqueness before union
                for (int i = 0; i < group.Count; i++)
                {
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        var a = gamesList[group[i]];
                        var b = gamesList[group[j]];
                        // Merge if from different stores or identical store ID
                        if (a.Store != b.Store || a.StoreId == b.StoreId)
                        {
                            Union(group[i], group[j]);
                        }
                    }
                }
            }
        }

        // 4. Group by root representative
        var clusters = new Dictionary<int, List<ProviderGame>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!clusters.TryGetValue(root, out var cluster))
            {
                cluster = new List<ProviderGame>();
                clusters[root] = cluster;
            }
            cluster.Add(gamesList[i]);
        }

        // 5. Build unified Game entities
        var resultGames = new List<Game>();
        foreach (var cluster in clusters.Values)
        {
            var primary = cluster.OrderByDescending(c => c.IsInstalled)
                                 .ThenBy(c => c.Store == StoreKind.Steam ? 0 : 1)
                                 .First();

            var game = new Game
            {
                Id = Guid.NewGuid(),
                Title = primary.Title,
                SortTitle = primary.SortTitle ?? primary.Title,
                Metadata = primary.Metadata
            };

            // Combine artwork
            var coverLocal = cluster.Select(c => c.Artwork.LocalCoverPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            var coverUrl = cluster.Select(c => c.Artwork.CoverUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
            var bgLocal = cluster.Select(c => c.Artwork.LocalBackgroundPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            var bgUrl = cluster.Select(c => c.Artwork.BackgroundUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
            var logoLocal = cluster.Select(c => c.Artwork.LocalLogoPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            var iconLocal = cluster.Select(c => c.Artwork.LocalIconPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));

            game.Artwork = new ArtworkSet
            {
                LocalCoverPath = coverLocal,
                CoverUrl = coverUrl,
                LocalBackgroundPath = bgLocal,
                BackgroundUrl = bgUrl,
                LocalLogoPath = logoLocal,
                LocalIconPath = iconLocal
            };

            // Deduplicate sources per storefront
            var sourcesByStore = new Dictionary<StoreKind, ProviderGame>();
            foreach (var pg in cluster)
            {
                if (!sourcesByStore.TryGetValue(pg.Store, out var existing))
                {
                    sourcesByStore[pg.Store] = pg;
                }
                else
                {
                    if (pg.IsInstalled && !existing.IsInstalled)
                    {
                        sourcesByStore[pg.Store] = pg;
                    }
                    else if (!string.IsNullOrEmpty(pg.LaunchUri) && string.IsNullOrEmpty(existing.LaunchUri))
                    {
                        sourcesByStore[pg.Store] = pg;
                    }
                }
            }

            foreach (var src in sourcesByStore.Values)
            {
                game.Sources.Add(src.ToSource());
            }

            resultGames.Add(game);
        }

        return resultGames.OrderBy(g => g.SortTitle).ToList();
    }

    public static string NormalizeTitle(string title)
    {
        var s = title.ToLowerInvariant();
        s = Regex.Replace(s, @"[™®©:_\-'""]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static (StoreKind Store, string StoreId)? ParseReleaseKey(string releaseKey)
    {
        var idx = releaseKey.IndexOf('_');
        if (idx <= 0) return null;

        var prefix = releaseKey[..idx].ToLowerInvariant();
        var id = releaseKey[(idx + 1)..];

        return prefix switch
        {
            "steam" => (StoreKind.Steam, id),
            "epic" => (StoreKind.Epic, id),
            "gog" => (StoreKind.Gog, id),
            "xboxone" => (StoreKind.Xbox, id),
            _ => null
        };
    }
}
