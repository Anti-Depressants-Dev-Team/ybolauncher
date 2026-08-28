namespace Launcher.Core.Services;

/// <summary>
/// "Start with Windows", via the per-user Run key.
/// <para>
/// HKCU rather than HKLM: the machine-wide key needs elevation, and this is a personal
/// preference, not a system policy. SPEC.md calls for the registry approach because the
/// app ships unpackaged - a packaged build would use a StartupTask instead.
/// </para>
/// </summary>
public interface IStartupService
{
    /// <summary>True when a Run entry for this app exists.</summary>
    bool IsEnabled();

    /// <summary>
    /// Adds or removes the Run entry. Returns false when the registry refused the write,
    /// which the Settings page surfaces rather than silently reverting the toggle.
    /// </summary>
    bool SetEnabled(bool enabled, bool startMinimized);

    /// <summary>
    /// True when a Run entry exists but points somewhere else - the app has been moved or
    /// copied since it was registered. Re-enabling repairs it.
    /// </summary>
    bool IsStale();
}
