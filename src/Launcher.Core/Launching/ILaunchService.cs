using Launcher.Core.Models;

namespace Launcher.Core.Launching;

/// <summary>Starts apps and opens their location in Explorer.</summary>
public interface ILaunchService
{
    /// <summary>
    /// Starts an entry. On success the entry's <see cref="AppEntry.LaunchCount"/> and
    /// <see cref="AppEntry.LastLaunchedUtc"/> are updated - persisting that is the
    /// caller's job.
    /// <para>Never throws; failures come back as a <see cref="LaunchResult"/>.</para>
    /// </summary>
    Task<LaunchResult> LaunchAsync(
        AppEntry entry,
        bool asAdministrator = false,
        CancellationToken cancellationToken = default);

    /// <summary>Opens Explorer with the entry's file selected.</summary>
    Task<LaunchResult> OpenFileLocationAsync(AppEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// False for packaged apps and protocol links: <c>AppListEntry.LaunchAsync</c> has no
    /// elevation option, and there is no process to elevate for a URI.
    /// </summary>
    bool CanLaunchAsAdministrator(AppEntry entry);

    /// <summary>False when there is no file on disk to reveal.</summary>
    bool CanOpenFileLocation(AppEntry entry);
}
