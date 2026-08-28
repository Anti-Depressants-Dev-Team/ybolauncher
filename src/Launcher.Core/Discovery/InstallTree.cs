namespace Launcher.Core.Discovery;

/// <summary>
/// Decides whether two executables belong to the same installed application.
/// <para>
/// Electron and Squirrel apps - Medal, Discord, GitHub Desktop and plenty more - keep a
/// versioned folder per release beside a stub, so one app owns
/// <c>…\Medal\current\Medal.exe</c> and <c>…\Medal\app-4.1.2\Medal.exe</c> at once.
/// Shortcuts to each are different targets and so different merge keys, and the app shows
/// up twice.
/// </para>
/// <para>
/// Two files are taken to be the same app when they share a folder that is specific enough
/// to *be* an install folder. That last part is the whole safety of it: without it, two
/// unrelated apps under <c>C:\Program Files</c> would look like one.
/// </para>
/// </summary>
public static class InstallTree
{
    /// <summary>
    /// A common ancestor this shallow says nothing - every app on the machine shares one.
    /// Counted in segments after the drive, so <c>C:\Program Files\App</c> is 2.
    /// </summary>
    private const int SmallestMeaningfulDepth = 2;

    /// <summary>
    /// Folders that hold many unrelated applications. A shared ancestor from this list is
    /// not evidence of anything, however deep it is.
    /// </summary>
    private static readonly HashSet<string> GenericRoots = BuildGenericRoots();

    /// <summary>
    /// True when both executables sit inside one application's install folder.
    /// </summary>
    public static bool ShareAnInstallFolder(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        string[] left = Segments(first);
        string[] right = Segments(second);

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        // Different drives cannot be one install.
        if (!string.Equals(left[0], right[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int shared = 0;
        int limit = Math.Min(left.Length, right.Length) - 1; // never count the file name

        while (shared < limit && string.Equals(left[shared], right[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        // shared counts the drive as well, so an install folder needs one more than depth.
        if (shared < SmallestMeaningfulDepth + 1)
        {
            return false;
        }

        string ancestor = string.Join(Path.DirectorySeparatorChar, left[..shared]);

        return !GenericRoots.Contains(ancestor.TrimEnd(Path.DirectorySeparatorChar));
    }

    private static string[] Segments(string path)
    {
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

            // A target read off a shortcut is always absolute. Anything else would be
            // resolved against the working directory, which would make two unrelated
            // scraps of text look like neighbours.
            if (!Path.IsPathRooted(expanded))
            {
                return [];
            }

            string full = Path.GetFullPath(expanded);

            return full.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static HashSet<string> BuildGenericRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Environment.SpecialFolder folder in new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.CommonDesktopDirectory,
        })
        {
            Add(Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify));
        }

        // Not a special folder, but where per-user installers put one folder per app.
        Add(Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            "Programs"));

        return roots;

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                roots.Add(path.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
    }
}
