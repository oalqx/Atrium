namespace Atrium.Core.Models;

public static class PreferredSourceResolver
{
    private static readonly IReadOnlyList<StoreKind> DefaultStoreOrder = new[]
    {
        StoreKind.Steam,
        StoreKind.Gog,
        StoreKind.Epic,
        StoreKind.Xbox,
        StoreKind.Local
    };

    /// <summary>
    /// Resolves the primary GameSource to use for launching and primary display.
    /// Priority:
    /// 1. Per-game preferred store override (if that source exists).
    /// 2. Installed sources ranked by global store preference.
    /// 3. Non-installed sources ranked by global store preference.
    /// 4. First available source.
    /// </summary>
    public static GameSource? Resolve(Game game, IReadOnlyList<StoreKind>? customOrder = null)
    {
        if (game.Sources.Count == 0)
        {
            return null;
        }

        if (game.Sources.Count == 1)
        {
            return game.Sources[0];
        }

        var order = customOrder is { Count: > 0 } ? customOrder : DefaultStoreOrder;

        // 1. Check user override
        if (game.User.PreferredStoreOverride.HasValue)
        {
            var userOverride = game.Sources.FirstOrDefault(s => s.Store == game.User.PreferredStoreOverride.Value);
            if (userOverride != null)
            {
                return userOverride;
            }
        }

        // 2. Separate into installed vs uninstalled
        var installed = game.Sources.Where(s => s.IsInstalled).ToList();
        var pool = installed.Count > 0 ? installed : game.Sources;

        // 3. Pick by store preference order
        foreach (var preferredStore in order)
        {
            var match = pool.FirstOrDefault(s => s.Store == preferredStore);
            if (match != null)
            {
                return match;
            }
        }

        return pool[0];
    }
}
