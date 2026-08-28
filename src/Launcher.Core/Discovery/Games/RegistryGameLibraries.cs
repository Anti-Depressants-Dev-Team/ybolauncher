using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// GOG Galaxy. Each installed game gets a registry key carrying its name, folder and
/// executable, and GOG games are DRM-free, so they are launched directly rather than
/// through the client.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GogLibrary : IGameLibrary
{
    public string Name => "GOG";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        foreach (string root in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            try
            {
                using RegistryKey? parent = Registry.LocalMachine.OpenSubKey(root);

                if (parent is null)
                {
                    continue;
                }

                foreach (string id in parent.GetSubKeyNames())
                {
                    using RegistryKey? key = parent.OpenSubKey(id);

                    if (key?.GetValue("gameName") is not string name || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string? path = key.GetValue("path") as string;
                    string? exe = key.GetValue("exe") as string;

                    // "exe" is usually already absolute, but is relative in older entries.
                    string? executable = exe switch
                    {
                        null or "" => GameExecutables.FindBest(path, name),
                        _ when Path.IsPathRooted(exe) => exe,
                        _ when !string.IsNullOrWhiteSpace(path) => Path.Combine(path, exe),
                        _ => null,
                    };

                    if (games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    games.Add(new GameEntry
                    {
                        Name = name,
                        LibraryName = "GOG",
                        ExecutablePath = executable,
                        InstallDirectory = path,
                    });
                }
            }
            catch (Exception)
            {
                // A registry view we cannot read simply yields no GOG games.
            }
        }

        return games;
    }
}

/// <summary>
/// Ubisoft Connect. The registry records only an install directory per game id, so the
/// name comes from the folder and the executable is guessed for the icon.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UbisoftLibrary : IGameLibrary
{
    public string Name => "Ubisoft Connect";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        try
        {
            using RegistryKey? parent = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");

            if (parent is null)
            {
                return games;
            }

            foreach (string id in parent.GetSubKeyNames())
            {
                using RegistryKey? key = parent.OpenSubKey(id);

                if (key?.GetValue("InstallDir") is not string installDir || string.IsNullOrWhiteSpace(installDir))
                {
                    continue;
                }

                string normalized = installDir.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                string name = SafeFolderName(normalized);

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                games.Add(new GameEntry
                {
                    Name = name,
                    LibraryName = "Ubisoft Connect",
                    LaunchUri = "uplay://launch/" + id + "/0",
                    InstallDirectory = normalized,
                    ExecutablePath = GameExecutables.FindBest(normalized, name),
                });
            }
        }
        catch (Exception)
        {
            // No Ubisoft games rather than a failed scan.
        }

        return games;
    }

    private static string SafeFolderName(string path)
    {
        try
        {
            return new DirectoryInfo(path).Name;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Battle.net games, found through their uninstall entries.
/// <para>
/// Battle.net's own catalog is a protobuf database with no stable public schema, so the
/// uninstall registry - which Blizzard's installers populate consistently - is the
/// dependable source. Games are launched by executable, which bootstraps the client.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BattleNetLibrary : IGameLibrary
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public string Name => "Battle.net";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        foreach (string root in UninstallRoots)
        {
            try
            {
                using RegistryKey? parent = Registry.LocalMachine.OpenSubKey(root);

                if (parent is null)
                {
                    continue;
                }

                foreach (string id in parent.GetSubKeyNames())
                {
                    using RegistryKey? key = parent.OpenSubKey(id);

                    if (key is null || !IsBlizzardEntry(key))
                    {
                        continue;
                    }

                    if (key.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // The Battle.net client itself is not a game.
                    if (name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string? installLocation = key.GetValue("InstallLocation") as string;
                    string? executable = CleanIconPath(key.GetValue("DisplayIcon") as string)
                        ?? GameExecutables.FindBest(installLocation, name);

                    games.Add(new GameEntry
                    {
                        Name = name,
                        LibraryName = "Battle.net",
                        ExecutablePath = executable,
                        InstallDirectory = installLocation,
                    });
                }
            }
            catch (Exception)
            {
                // Skip a registry view we cannot read.
            }
        }

        return games;
    }

    private static bool IsBlizzardEntry(RegistryKey key)
    {
        try
        {
            string publisher = key.GetValue("Publisher") as string ?? string.Empty;
            string uninstall = key.GetValue("UninstallString") as string ?? string.Empty;

            return publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase)
                || uninstall.Contains("Battle.net", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>DisplayIcon is often "path\game.exe,0"; the index is not part of the path.</summary>
    private static string? CleanIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string path = value.Trim().Trim('"');
        int comma = path.LastIndexOf(',');

        if (comma > 2)
        {
            path = path[..comma];
        }

        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}

/// <summary>
/// Rockstar Games Launcher. Each installed title gets a key under
/// <c>SOFTWARE\Rockstar Games</c> holding its install folder.
/// <para>
/// Games are launched by executable rather than a protocol, which is exactly what the
/// launcher's own Start Menu shortcuts do - the game's boot executable brings up the
/// launcher for sign-in by itself.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RockstarLibrary : IGameLibrary
{
    /// <summary>Keys under the same root that are not games.</summary>
    private static readonly string[] ExcludedNameFragments =
    [
        "Launcher",
        "Social Club",
        "Subscription",
        "Redistributable",
    ];

    public string Name => "Rockstar Games";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        foreach (string root in new[] { @"SOFTWARE\WOW6432Node\Rockstar Games", @"SOFTWARE\Rockstar Games" })
        {
            try
            {
                using RegistryKey? parent = Registry.LocalMachine.OpenSubKey(root);

                if (parent is null)
                {
                    continue;
                }

                foreach (string title in parent.GetSubKeyNames())
                {
                    using RegistryKey? key = parent.OpenSubKey(title);

                    if (key is null)
                    {
                        continue;
                    }

                    GameEntry? game = BuildGame(title, key.GetValue("InstallFolder") as string);

                    if (game is not null
                        && !games.Any(g => string.Equals(g.Name, game.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        games.Add(game);
                    }
                }
            }
            catch (Exception)
            {
                // A registry view we cannot read yields no Rockstar games.
            }
        }

        return games;
    }

    /// <summary>
    /// Turns one title key into a game, or null when the key is not a game or has nothing
    /// to run.
    /// </summary>
    public static GameEntry? BuildGame(string title, string? installFolder)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(installFolder))
        {
            return null;
        }

        if (ExcludedNameFragments.Any(f => title.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        // The value routinely carries a trailing separator.
        string folder = installFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (GameExecutables.FindBest(folder, title) is not { } executable)
        {
            return null;
        }

        return new GameEntry
        {
            Name = title,
            LibraryName = "Rockstar Games",
            ExecutablePath = executable,
            InstallDirectory = folder,
        };
    }
}
