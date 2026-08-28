namespace Launcher.Core.Discovery;

/// <summary>
/// Watches for apps being installed or removed so the catalog stays fresh without the user
/// pressing Rescan.
/// <para>
/// Both sources are noisy: an installer writes many shortcuts in a burst, and the package
/// catalog reports progress repeatedly for one install. Changes are therefore coalesced
/// and reported once the dust settles.
/// </para>
/// </summary>
public interface IAppWatcherService : IDisposable
{
    /// <summary>True once watching has started and at least one source attached.</summary>
    bool IsWatching { get; }

    void StartWatching();

    void StopWatching();

    /// <summary>
    /// Raised on a background thread after a quiet period follows one or more changes.
    /// </summary>
    event EventHandler? ChangeDetected;
}
