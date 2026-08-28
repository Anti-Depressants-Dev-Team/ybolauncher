using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>Progress report emitted while a scan runs.</summary>
/// <param name="SourceName">Human-readable source, e.g. "Start Menu".</param>
/// <param name="Completed">Items processed so far.</param>
/// <param name="Total">Items expected, or 0 when not yet known.</param>
public sealed record DiscoveryProgress(string SourceName, int Completed, int Total);

/// <summary>Options handed to every source for one scan.</summary>
/// <param name="IconPixelSize">Edge length to extract icons at, in physical pixels.</param>
/// <param name="Progress">Optional progress sink. Reports may arrive on any thread.</param>
public sealed record DiscoveryContext(int IconPixelSize, IProgress<DiscoveryProgress>? Progress);

/// <summary>
/// One place apps can be found. Sources return raw entries; filtering, deduplication and
/// merging with the existing catalog are the discovery service's job, not theirs.
/// </summary>
public interface IAppSource
{
    AppSource Kind { get; }

    /// <summary>Name used in progress reports.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Enumerates apps. Implementations must not throw for ordinary trouble - a source
    /// that fails should log and return what it managed to collect.
    /// </summary>
    Task<IReadOnlyList<AppEntry>> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default);
}
