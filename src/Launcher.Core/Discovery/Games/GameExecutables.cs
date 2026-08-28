namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Guesses a game's main executable from its install folder.
/// <para>
/// Some launchers record only an install directory, and a game folder is full of
/// executables - crash handlers, redistributables, uninstallers. This picks the most
/// plausible one, which is only ever used for the icon and for "open file location";
/// launching still goes through the launcher's protocol.
/// </para>
/// </summary>
public static class GameExecutables
{
    /// <summary>Executables that are never the game itself.</summary>
    private static readonly string[] ExcludedNameFragments =
    [
        "unins", "uninstall", "setup", "install", "vcredist", "directx", "dxsetup",
        "crashhandler", "crashreport", "cleanup", "activation", "helper", "config",
        "redist", "dotnetfx", "touchup", "patch", "updater", "eula",
    ];

    /// <summary>
    /// Best candidate, or null. Only the folder root and one level down are searched -
    /// deep recursion through a game's asset tree is slow and rarely helps.
    /// </summary>
    public static string? FindBest(string? installDirectory, string? preferredName = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(installDirectory))
            {
                return null;
            }

            // A file named after the game (or its folder) is almost always the right one.
            string wanted = string.IsNullOrWhiteSpace(preferredName)
                ? new DirectoryInfo(installDirectory).Name
                : preferredName;

            var candidates = Directory
                .EnumerateFiles(installDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                .Concat(EnumerateOneLevelDown(installDirectory))
                .Where(path => !IsExcluded(path))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            string? named = candidates.FirstOrDefault(
                path => Path.GetFileNameWithoutExtension(path)
                    .Equals(Sanitize(wanted), StringComparison.OrdinalIgnoreCase));

            if (named is not null)
            {
                return named;
            }

            // Otherwise the largest executable, which for a game is essentially always
            // the engine binary rather than a tool shipped alongside it.
            return candidates
                .Select(path => (Path: path, Size: SafeLength(path)))
                .OrderByDescending(c => c.Size)
                .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .First()
                .Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> EnumerateOneLevelDown(string root)
    {
        List<string> results = [];

        try
        {
            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                try
                {
                    results.AddRange(Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly));
                }
                catch (Exception)
                {
                    // Skip folders we cannot read.
                }
            }
        }
        catch (Exception)
        {
            // Skip a root we cannot enumerate.
        }

        return results;
    }

    private static bool IsExcluded(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        return ExcludedNameFragments.Any(
            fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Strips characters a folder name may carry that a file name will not.</summary>
    private static string Sanitize(string value) =>
        new([.. value.Where(c => !char.IsWhiteSpace(c) && c is not (':' or '-' or '\'' or '!'))]);

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
