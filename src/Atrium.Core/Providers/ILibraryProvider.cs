using Atrium.Core.Models;

namespace Atrium.Core.Providers;

public enum ProviderStatus
{
    Available,
    NotInstalled,
    NeedsConfiguration,
    Error
}

public interface ILibraryProvider
{
    StoreKind Store { get; }
    string DisplayName { get; }
    Task<ProviderStatus> GetStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProviderGame>> GetGamesAsync(CancellationToken ct = default);
}
