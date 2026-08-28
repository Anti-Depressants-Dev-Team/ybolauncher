using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Amazon Games, found through the uninstall entries its installer writes.
/// <para>
/// The app's own index is a SQLite database, so the per-user uninstall registry is used
/// instead: Amazon writes one key per game named <c>AmazonGames/&lt;title&gt;</c>, carrying
/// the display name, the install folder and - in the uninstall command - the product id
/// the launch protocol needs.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmazonGamesLibrary : IGameLibrary
{
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Amazon prefixes its uninstall keys with this.</summary>
    private const string KeyPrefix = "AmazonGames/";

    public string Name => "Amazon Games";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        // Amazon installs per user, but the machine hive is checked too in case a future
        // build changes that.
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using RegistryKey? parent = hive.OpenSubKey(UninstallRoot);

                if (parent is null)
                {
                    continue;
                }

                foreach (string id in parent.GetSubKeyNames())
                {
                    using RegistryKey? key = parent.OpenSubKey(id);

                    if (key is null)
                    {
                        continue;
                    }

                    string uninstall = key.GetValue("UninstallString") as string ?? string.Empty;

                    if (!id.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)
                        && !uninstall.Contains("Amazon Game Remover", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    GameEntry? game = BuildGame(
                        key.GetValue("DisplayName") as string ?? id[KeyPrefix.Length..],
                        key.GetValue("InstallLocation") as string,
                        uninstall);

                    if (game is not null
                        && !games.Any(g => string.Equals(g.Name, game.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        games.Add(game);
                    }
                }
            }
            catch (Exception)
            {
                // A hive we cannot read simply yields no Amazon games.
            }
        }

        return games;
    }

    /// <summary>
    /// Turns one uninstall entry into a game, or null when it is not a playable one.
    /// </summary>
    public static GameEntry? BuildGame(string? displayName, string? installLocation, string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        // The launcher itself is not a game.
        if (displayName.Equals("Amazon Games", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Amazon Games App", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? productId = ParseProductId(uninstallString);
        string? executable = FindExecutable(installLocation, displayName);

        if (productId is null && executable is null)
        {
            // Nothing to launch by either route.
            return null;
        }

        return new GameEntry
        {
            Name = displayName,
            LibraryName = "Amazon Games",

            // The protocol starts the game through the app, which is what handles its
            // entitlement check.
            LaunchUri = productId is null ? null : "amazon-games://play/" + productId,
            ExecutablePath = executable,
            InstallDirectory = installLocation,
        };
    }

    /// <summary>
    /// Pulls the product id out of the uninstall command, which looks like
    /// <c>"…\Amazon Game Remover.exe" -m Game -p amzn1.adg.product.…</c>.
    /// </summary>
    public static string? ParseProductId(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return null;
        }

        string[] parts = uninstallString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] is "-p" or "-P")
            {
                string id = parts[i + 1].Trim('"');

                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
        }

        return null;
    }

    /// <summary>
    /// Every Amazon install carries a <c>fuel.json</c> naming the executable the app runs.
    /// Searching the folder is the fallback for an install written by an older client.
    /// </summary>
    private static string? FindExecutable(string? installLocation, string name)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        try
        {
            string fuel = Path.Combine(installLocation, "fuel.json");

            if (File.Exists(fuel) && ParseFuelCommand(File.ReadAllText(fuel)) is { } command)
            {
                string full = Path.Combine(installLocation, command.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(full))
                {
                    return full;
                }
            }
        }
        catch (Exception)
        {
            // Fall back to searching the folder.
        }

        return GameExecutables.FindBest(installLocation, name);
    }

    /// <summary>Reads the launch command out of a <c>fuel.json</c>.</summary>
    public static string? ParseFuelCommand(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Main", out JsonElement main)
                || main.ValueKind != JsonValueKind.Object
                || !main.TryGetProperty("Command", out JsonElement command)
                || command.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? value = command.GetString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
