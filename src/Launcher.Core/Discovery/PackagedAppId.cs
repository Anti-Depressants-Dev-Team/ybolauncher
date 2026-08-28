namespace Launcher.Core.Discovery;

/// <summary>
/// Tells a real packaged (MSIX/Store) AUMID apart from the legacy Win32 kind.
/// <para>
/// Plenty of ordinary desktop apps stamp an explicit AppUserModelID on their Start Menu
/// shortcuts so the taskbar groups their windows correctly - Firefox uses
/// <c>308046B0AF4A39CB</c>, Edge uses <c>MSEdge</c>, Visual Studio uses
/// <c>VisualStudio.257105d1</c>. None of those are in the package catalog, so treating
/// them as packaged apps would throw away the shortcut's target path and leave an entry
/// that cannot be launched at all.
/// </para>
/// <para>
/// A packaged AUMID is always <c>PackageFamilyName!ApplicationId</c>, and the family name
/// is always <c>Name_PublisherId</c>. Requiring both separators is enough to tell them
/// apart.
/// </para>
/// </summary>
public static class PackagedAppId
{
    public static bool IsPackagedAumid(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return false;
        }

        int separator = appUserModelId.IndexOf('!', StringComparison.Ordinal);

        // Needs a non-empty family name before the '!' and a non-empty app id after it.
        if (separator <= 0 || separator >= appUserModelId.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> familyName = appUserModelId.AsSpan(0, separator);
        int publisherSeparator = familyName.LastIndexOf('_');

        return publisherSeparator > 0 && publisherSeparator < familyName.Length - 1;
    }
}
