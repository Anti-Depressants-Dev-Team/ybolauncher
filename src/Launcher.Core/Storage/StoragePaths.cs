namespace Launcher.Core.Storage;

/// <summary>
/// Resolves where launcher state lives on disk.
/// <para>
/// Normally that is <c>%LocalAppData%\YBO Launcher\</c>. If a <c>portable.txt</c> sits
/// next to the executable, everything moves into the application folder instead so the
/// launcher can run from a USB stick without leaving traces on the host.
/// </para>
/// </summary>
public sealed class StoragePaths
{
    public StoragePaths(string root, bool isPortable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
        IsPortable = isPortable;
    }

    /// <summary>Directory containing every JSON document and the icon cache.</summary>
    public string Root { get; }

    /// <summary>True when state is stored beside the executable rather than in %LocalAppData%.</summary>
    public bool IsPortable { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string AppsFile => Path.Combine(Root, "apps.json");

    public string TabsFile => Path.Combine(Root, "tabs.json");

    public string IconCacheDirectory => Path.Combine(Root, "iconcache");

    /// <summary>
    /// Resolves the paths for the currently running executable.
    /// </summary>
    public static StoragePaths CreateDefault()
    {
        string exeDirectory = AppContext.BaseDirectory;
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        return Resolve(exeDirectory, localAppData);
    }

    /// <summary>
    /// Pure resolution logic, split out from <see cref="CreateDefault"/> so it can be tested
    /// against arbitrary directories.
    /// </summary>
    public static StoragePaths Resolve(string exeDirectory, string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);

        if (IsPortableInstall(exeDirectory))
        {
            return new StoragePaths(Path.Combine(exeDirectory, "data"), isPortable: true);
        }

        return new StoragePaths(Path.Combine(localAppData, AppInfo.DataFolderName), isPortable: false);
    }

    /// <summary>
    /// True when the portable marker file exists beside the executable. A probe failure
    /// (permissions, missing directory) is treated as "not portable" rather than an error.
    /// </summary>
    public static bool IsPortableInstall(string exeDirectory)
    {
        try
        {
            return File.Exists(Path.Combine(exeDirectory, AppInfo.PortableMarkerFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the root and icon cache directories if they are missing.
    /// Returns false when the directories could not be created, leaving the caller to run
    /// in-memory rather than crash.
    /// </summary>
    public bool EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(IconCacheDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
