using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>Finds installed apps and owns the persisted catalog.</summary>
public interface IAppDiscoveryService
{
    /// <summary>
    /// Every known entry, including ones the junk filter rejected and ones the user hid.
    /// Callers decide what to show; see <see cref="AppEntry.IsVisibleOnHome"/>.
    /// </summary>
    IReadOnlyList<AppEntry> Entries { get; }

    /// <summary>True while a scan is in flight.</summary>
    bool IsScanning { get; }

    /// <summary>Raised on the thread that completed the change, after <see cref="Entries"/> is replaced.</summary>
    event EventHandler? EntriesChanged;

    /// <summary>
    /// Loads apps.json so the UI has something to show immediately. Returns false when
    /// there was no usable cache and a scan is required.
    /// </summary>
    Task<bool> LoadCachedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs every enabled source, deduplicates, merges the result with existing user
    /// edits, and persists the catalog.
    /// </summary>
    Task ScanAsync(
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the catalog after a user edit - a rename, a hide, a custom icon, or an
    /// updated launch count. Does not raise <see cref="EntriesChanged"/>: the caller
    /// already knows what it changed, and a full list rebuild would lose selection.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
