namespace Launcher.Core;

/// <summary>
/// Product-level constants. Anything user-visible or path-forming lives here so the
/// product can be renamed in exactly one place.
/// </summary>
public static class AppInfo
{
    /// <summary>Display name used in the title bar, tray tooltip and About page.</summary>
    public const string ProductName = "YBO Launcher";

    /// <summary>Folder name under %LocalAppData% (and the registry Run value name).</summary>
    public const string DataFolderName = "YBO Launcher";

    /// <summary>
    /// When a file with this name sits next to the executable, all state is stored in the
    /// application folder instead of %LocalAppData% (portable mode).
    /// </summary>
    public const string PortableMarkerFileName = "portable.txt";
}
