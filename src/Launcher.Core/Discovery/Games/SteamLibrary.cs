using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Steam, read from its own bookkeeping: <c>libraryfolders.vdf</c> lists the library roots
/// and each <c>appmanifest_*.acf</c> describes one installed app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SteamLibrary : IGameLibrary
{
    /// <summary>StateFlags bit meaning "fully installed". Partial downloads are skipped.</summary>
    private const int StateFullyInstalled = 4;

    /// <summary>
    /// Entries Steam keeps in the library that are not games: shared redistributables and
    /// the compatibility runtimes.
    /// </summary>
    private static readonly HashSet<string> ExcludedAppIds = new(StringComparer.Ordinal)
    {
        "228980", // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime 1.0
        "1391110", // Steam Linux Runtime 2.0
        "1628350", // Steam Linux Runtime 3.0
    };

    private static readonly string[] ExcludedNamePrefixes =
    [
        "Steam Linux Runtime",
        "Proton ",
        "Proton Experimental",
        "Steamworks Common",
    ];

    private readonly Func<string?> _findSteamPath;

    public SteamLibrary(Func<string?>? findSteamPath = null) =>
        _findSteamPath = findSteamPath ?? FindSteamPath;

    public string Name => "Steam";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        try
        {
            if (_findSteamPath() is not { } steamPath || !Directory.Exists(steamPath))
            {
                return [];
            }

            var games = new List<GameEntry>();

            foreach (string library in GetLibraryPaths(steamPath))
            {
                string appsFolder = Path.Combine(library, "steamapps");

                if (!Directory.Exists(appsFolder))
                {
                    continue;
                }

                foreach (string manifest in Directory.EnumerateFiles(appsFolder, "appmanifest_*.acf"))
                {
                    try
                    {
                        GameEntry? game = ParseAppManifest(File.ReadAllText(manifest), library);

                        if (game is not null)
                        {
                            games.Add(game with { IconPath = FindCachedIcon(steamPath, manifest) });
                        }
                    }
                    catch (Exception)
                    {
                        // One unreadable manifest costs one game, not the library.
                    }
                }
            }

            return games;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Steam's install folder, from the per-user key first then the machine key.</summary>
    public static string? FindSteamPath()
    {
        foreach ((RegistryKey root, string path, string value) in new[]
        {
            (Registry.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        })
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(path);

                if (key?.GetValue(value) is string found && !string.IsNullOrWhiteSpace(found))
                {
                    // The per-user key stores forward slashes.
                    return found.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            catch (Exception)
            {
                // Try the next location.
            }
        }

        return null;
    }

    /// <summary>
    /// Every library root, always including Steam's own folder. Games are commonly spread
    /// across several drives.
    /// </summary>
    private static List<string> GetLibraryPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };

        try
        {
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (File.Exists(vdf))
            {
                foreach (string extra in ParseLibraryFolders(File.ReadAllText(vdf)))
                {
                    if (!paths.Contains(extra, StringComparer.OrdinalIgnoreCase))
                    {
                        paths.Add(extra);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall back to Steam's own folder.
        }

        return paths;
    }

    /// <summary>
    /// Reads library roots out of <c>libraryfolders.vdf</c>.
    /// <para>
    /// Two formats are in the wild: older Steam wrote <c>"1" "D:\\Games"</c> directly,
    /// newer Steam writes a block per library with a <c>path</c> inside it.
    /// </para>
    /// </summary>
    public static List<string> ParseLibraryFolders(string vdf)
    {
        var paths = new List<string>();
        VdfNode? root = VdfParser.Parse(vdf);

        if (root?["libraryfolders"] is not { } folders)
        {
            return paths;
        }

        foreach (KeyValuePair<string, VdfNode> entry in folders.Children)
        {
            // Skip bookkeeping keys like "contentstatsid".
            if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            string? path = entry.Value.Value ?? entry.Value.GetString("path");

            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Turns one <c>appmanifest_*.acf</c> into a game, or null when it is not an installed
    /// game.
    /// </summary>
    public static GameEntry? ParseAppManifest(string acf, string libraryPath)
    {
        VdfNode? state = VdfParser.Parse(acf)?["AppState"];

        if (state is null)
        {
            return null;
        }

        string? appId = state.GetString("appid");
        string? name = state.GetString("name");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (ExcludedAppIds.Contains(appId) || IsExcludedName(name))
        {
            return null;
        }

        // A game still downloading has other bits set; only 4 means it is playable.
        if (int.TryParse(state.GetString("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags)
            && (flags & StateFullyInstalled) == 0)
        {
            return null;
        }

        string? installDir = state.GetString("installdir");
        string? fullInstallPath = string.IsNullOrWhiteSpace(installDir)
            ? null
            : Path.Combine(libraryPath, "steamapps", "common", installDir);

        return new GameEntry
        {
            Name = name,
            LibraryName = "Steam",
            LaunchUri = "steam://rungameid/" + appId,
            InstallDirectory = fullInstallPath,
            ExecutablePath = fullInstallPath,
        };
    }

    private static bool IsExcludedName(string name) =>
        ExcludedNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Steam caches a per-game icon. The layout has changed across client versions, so
    /// several known shapes are tried before giving up.
    /// </summary>
    private static string? FindCachedIcon(string steamPath, string manifestPath)
    {
        try
        {
            string fileName = Path.GetFileNameWithoutExtension(manifestPath);
            string appId = fileName["appmanifest_".Length..];
            string cache = Path.Combine(steamPath, "appcache", "librarycache");

            if (!Directory.Exists(cache))
            {
                return null;
            }

            // Older clients: one flat file per app.
            foreach (string suffix in new[] { "_icon.jpg", "_icon.png", "_logo.png" })
            {
                string flat = Path.Combine(cache, appId + suffix);

                if (File.Exists(flat))
                {
                    return flat;
                }
            }

            // Newer clients: a folder per app with hashed file names.
            string folder = Path.Combine(cache, appId);

            if (Directory.Exists(folder))
            {
                return Directory.EnumerateFiles(folder, "*.jpg")
                    .Concat(Directory.EnumerateFiles(folder, "*.png"))
                    .FirstOrDefault();
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
