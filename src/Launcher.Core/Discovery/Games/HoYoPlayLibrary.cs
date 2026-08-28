using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// HoYoPlay, the HoYoverse launcher, and the older per-game launchers it replaced.
/// <para>
/// Two registry sources are read. HoYoPlay records an install path per game under its own
/// key - <c>Cognosphere\HYP</c> for the global client, <c>miHoYo\HYP</c> for the Chinese
/// one - and every install also writes an ordinary uninstall entry. The two overlap, which
/// is deliberate: a game installed before HoYoPlay existed only has the uninstall entry.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HoYoPlayLibrary : IGameLibrary
{
    /// <summary>
    /// The executable each game ships, and the name to show for it. HoYoverse install
    /// folders are named after the build rather than the game ("Genshin Impact game"), and
    /// the Chinese builds use different executable names for the same title, so the
    /// executable is what the name is derived from.
    /// </summary>
    private static readonly (string Executable, string Title)[] KnownGames =
    [
        ("GenshinImpact.exe", "Genshin Impact"),
        ("YuanShen.exe", "Genshin Impact"),
        ("StarRail.exe", "Honkai: Star Rail"),
        ("BH3.exe", "Honkai Impact 3rd"),
        ("ZenlessZoneZero.exe", "Zenless Zone Zero"),
        ("NAP.exe", "Zenless Zone Zero"),
    ];

    /// <summary>Where HoYoPlay itself records install paths.</summary>
    private static readonly string[] LauncherKeys =
    [
        @"Software\Cognosphere\HYP",
        @"Software\miHoYo\HYP",
    ];

    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public string Name => "HoYoPlay";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        foreach ((string? path, string? displayName) in CollectInstalls())
        {
            GameEntry? game = BuildGame(path, displayName);

            if (game is null)
            {
                continue;
            }

            // A game found through both its HoYoPlay key and its uninstall entry is one
            // game.
            if (!games.Any(g => string.Equals(g.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                games.Add(game);
            }
        }

        return games;
    }

    /// <summary>Every install path the registry knows about, with a name when it has one.</summary>
    private static List<(string? Path, string? DisplayName)> CollectInstalls()
    {
        var installs = new List<(string? Path, string? DisplayName)>();

        foreach (string root in LauncherKeys)
        {
            try
            {
                using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(root);

                if (parent is not null)
                {
                    CollectInstallPaths(parent, installs, depth: 0);
                }
            }
            catch (Exception)
            {
                // A key we cannot read simply contributes nothing.
            }
        }

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

                    if (key is null || !IsHoYoverseEntry(key))
                    {
                        continue;
                    }

                    // HoYoPlay writes InstallPath; ordinary installers write
                    // InstallLocation.
                    string? path = key.GetValue("InstallPath") as string
                        ?? key.GetValue("InstallLocation") as string;

                    installs.Add((path, key.GetValue("DisplayName") as string ?? id));
                }
            }
            catch (Exception)
            {
                // Skip a registry view we cannot read.
            }
        }

        return installs;
    }

    /// <summary>
    /// Walks HoYoPlay's own key for install paths. The games sit one or two levels down,
    /// keyed by region and product, so the tree is searched rather than assumed.
    /// </summary>
    private static void CollectInstallPaths(
        RegistryKey key,
        List<(string? Path, string? DisplayName)> into,
        int depth)
    {
        if (depth > 3)
        {
            return;
        }

        try
        {
            foreach (string value in new[] { "GameInstallPath", "InstallPath" })
            {
                if (key.GetValue(value) is string path && !string.IsNullOrWhiteSpace(path))
                {
                    into.Add((path, null));
                }
            }

            foreach (string name in key.GetSubKeyNames())
            {
                using RegistryKey? child = key.OpenSubKey(name);

                if (child is not null)
                {
                    CollectInstallPaths(child, into, depth + 1);
                }
            }
        }
        catch (Exception)
        {
            // Whatever was collected so far still stands.
        }
    }

    /// <summary>
    /// Turns one install path into a game, or null when there is no game executable in it.
    /// </summary>
    public static GameEntry? BuildGame(string? installPath, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        // The launcher is not a game, and neither is the redistributable it installs.
        if (!string.IsNullOrWhiteSpace(displayName)
            && (displayName.Contains("HoYoPlay", StringComparison.OrdinalIgnoreCase)
                || displayName.Contains("Launcher", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string folder = installPath.Trim('"')
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string? executable = FindGameExecutable(folder);

        if (executable is null)
        {
            return null;
        }

        string name = TitleFor(executable) ?? FirstNonEmpty(displayName, SafeFolderName(folder));

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // The game runs directly - the launcher has no documented protocol for starting
        // one, and these executables bring up their own updater and sign-in.
        return new GameEntry
        {
            Name = name,
            LibraryName = "HoYoPlay",
            ExecutablePath = executable,
            InstallDirectory = Path.GetDirectoryName(executable) ?? folder,
        };
    }

    /// <summary>
    /// Looks for a known game executable in the folder and one level below it, which is
    /// where HoYoPlay puts the build folder.
    /// </summary>
    private static string? FindGameExecutable(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            foreach ((string executable, _) in KnownGames)
            {
                string direct = Path.Combine(folder, executable);

                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                foreach ((string executable, _) in KnownGames)
                {
                    string nested = Path.Combine(child, executable);

                    if (File.Exists(nested))
                    {
                        return nested;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the generic search.
        }

        // A game this build has never heard of still deserves a tile.
        return GameExecutables.FindBest(folder, SafeFolderName(folder));
    }

    /// <summary>The published name for a known executable, or null for anything else.</summary>
    private static string? TitleFor(string executablePath)
    {
        string name = Path.GetFileName(executablePath);

        foreach ((string executable, string title) in KnownGames)
        {
            if (string.Equals(name, executable, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        return null;
    }

    private static bool IsHoYoverseEntry(RegistryKey key)
    {
        try
        {
            string publisher = key.GetValue("Publisher") as string ?? string.Empty;

            return publisher.Contains("miHoYo", StringComparison.OrdinalIgnoreCase)
                || publisher.Contains("Cognosphere", StringComparison.OrdinalIgnoreCase)
                || publisher.Contains("HoYoverse", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

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
