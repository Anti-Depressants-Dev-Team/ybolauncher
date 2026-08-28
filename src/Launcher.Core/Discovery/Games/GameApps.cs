using Launcher.Core.Models;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Marks apps that are games even though no game launcher reports them.
/// <para>
/// Some games install like ordinary desktop software: they have a Start Menu shortcut and
/// nothing else, so the only thing that finds them is the Start Menu walk, which cannot
/// tell a game from a text editor. Minecraft launchers are the clear case - Prism Launcher
/// and Lunar Client are how people play the game, but no store knows about them.
/// </para>
/// <para>
/// This is a list of known names rather than a guess at what a game looks like. Guessing
/// would be worse than not marking: a false positive puts a text editor in the Games tab,
/// where it is obviously wrong and cannot be corrected.
/// </para>
/// </summary>
public static class GameApps
{
    /// <summary>
    /// Executable file names, which are more dependable than display names: an app can be
    /// renamed in its shortcut, and translations differ, but the binary keeps its name.
    /// </summary>
    private static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    {
        "prismlauncher.exe",
        "lunarclient.exe",
        "minecraft.exe",
        "minecraftlauncher.exe",
        "atlauncher.exe",
        "gdlauncher.exe",
        "multimc.exe",
        "modrinth app.exe",
        "technic launcher.exe",
        "curseforge.exe",
        "badlion client.exe",
        "feather launcher.exe",
        "roblox player.exe",
        "robloxplayerbeta.exe",
    };

    /// <summary>
    /// Display names, for the same apps reached by a shortcut with no readable target -
    /// and for the few whose executable name is too generic to match on.
    /// </summary>
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "prism launcher",
        "lunar client",
        "minecraft",
        "minecraft launcher",
        "minecraft: java edition",
        "atlauncher",
        "gdlauncher",
        "multimc",
        "modrinth app",
        "technic launcher",
        "badlion client",
        "feather launcher",
        "roblox",
    };

    /// <summary>True when this entry is a game the launcher knows by name.</summary>
    public static bool IsKnownGame(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Matches(entry.TargetPath) || Matches(entry.ShortcutPath))
        {
            return true;
        }

        return Names.Contains(Normalize(entry.OriginalName))
            || Names.Contains(Normalize(entry.DisplayName));
    }

    private static bool Matches(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Executables.Contains(Path.GetFileName(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
