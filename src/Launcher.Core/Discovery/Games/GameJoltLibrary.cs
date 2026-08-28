using System.Text.Json;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Game Jolt, read from the client's own data store in <c>%AppData%\game-jolt-client</c>.
/// <para>
/// The client keeps two JSON files with a <c>.wttf</c> extension: <c>packages.wttf</c> is
/// one record per install and <c>games.wttf</c> holds the titles. Both have been written
/// as a keyed map and as a plain array across client versions, so the reader accepts
/// either shape rather than assuming one.
/// </para>
/// </summary>
public sealed class GameJoltLibrary : IGameLibrary
{
    private readonly Func<string?> _findClientFolder;

    public GameJoltLibrary(Func<string?>? findClientFolder = null) =>
        _findClientFolder = findClientFolder ?? FindClientFolder;

    public string Name => "Game Jolt";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        try
        {
            if (_findClientFolder() is not { } folder || !Directory.Exists(folder))
            {
                return [];
            }

            string? packages = ReadStore(folder, "packages");

            if (packages is null)
            {
                return [];
            }

            var games = new List<GameEntry>();

            foreach (GameEntry game in ParsePackages(packages, ReadStore(folder, "games")))
            {
                // The client leaves a record behind when a game is removed from disk.
                if (game.InstallDirectory is not { } directory || !Directory.Exists(directory))
                {
                    continue;
                }

                string? executable = game.ExecutablePath is { } recorded && File.Exists(recorded)
                    ? recorded
                    : GameExecutables.FindBest(directory, game.Name);

                if (executable is null)
                {
                    continue;
                }

                games.Add(game with { ExecutablePath = executable });
            }

            return games;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>The Game Jolt client's data folder.</summary>
    public static string? FindClientFolder()
    {
        try
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return Path.Combine(appData, "game-jolt-client");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads one store file, accepting the plain <c>.json</c> name as a fallback.</summary>
    private static string? ReadStore(string folder, string name)
    {
        foreach (string candidate in new[] { name + ".wttf", name + ".json" })
        {
            try
            {
                string path = Path.Combine(folder, candidate);

                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch (Exception)
            {
                // Try the next name.
            }
        }

        return null;
    }

    /// <summary>
    /// Turns the package store into games. The install directory is reported as recorded;
    /// the caller checks it still exists.
    /// </summary>
    public static List<GameEntry> ParsePackages(string packagesJson, string? gamesJson)
    {
        var games = new List<GameEntry>();
        Dictionary<string, string> titles = ParseGameTitles(gamesJson);

        foreach (JsonElement package in EnumerateRecords(packagesJson))
        {
            // A package still downloading, patching or being removed is not playable.
            if (GetString(package, "install_state") is { Length: > 0 }
                || IsTrue(package, "is_removed"))
            {
                continue;
            }

            string? directory = GetString(package, "install_dir");

            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            // The game's own title is the one the user knows; a package is often named
            // after the build or the platform.
            string? name = GetString(package, "game_id") is { } gameId && titles.TryGetValue(gameId, out string? title)
                ? title
                : GetString(package, "title");

            name ??= new DirectoryInfo(directory).Name;

            games.Add(new GameEntry
            {
                Name = name,
                LibraryName = "Game Jolt",
                ExecutablePath = FindExecutable(package, directory),
                InstallDirectory = directory,
            });
        }

        return games;
    }

    /// <summary>Game id to title, so a package can be named after its game.</summary>
    private static Dictionary<string, string> ParseGameTitles(string? gamesJson)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(gamesJson))
        {
            return titles;
        }

        foreach (JsonElement game in EnumerateRecords(gamesJson))
        {
            if (GetString(game, "id") is { } id && GetString(game, "title") is { } title)
            {
                titles[id] = title;
            }
        }

        return titles;
    }

    /// <summary>
    /// The launch options record the executable relative to the install folder. It is only
    /// a hint - the caller falls back to searching the folder when the file is missing.
    /// </summary>
    private static string? FindExecutable(JsonElement package, string directory)
    {
        string? relative = GetString(package, "executable_path");

        if (relative is null
            && package.TryGetProperty("launch_options", out JsonElement options)
            && options.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement option in options.EnumerateArray())
            {
                relative = GetString(option, "executable_path");

                if (relative is not null)
                {
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        try
        {
            return Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Yields every record in a store, whether it is an array, a map of id to record, or
    /// either of those wrapped in a container property.
    /// </summary>
    private static List<JsonElement> EnumerateRecords(string json)
    {
        var records = new List<JsonElement>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Collect(document.RootElement.Clone(), records, depth: 0);
        }
        catch (JsonException)
        {
            // An unreadable store means no games from this launcher, not a failed scan.
        }

        return records;

        static void Collect(JsonElement element, List<JsonElement> into, int depth)
        {
            // A container holding a keyed map is the deepest shape seen; anything
            // further down is not a store.
            if (depth > 2)
            {
                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        into.Add(item.Clone());
                    }
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // A record carries an id; a container carries records.
            if (IsRecord(element))
            {
                into.Add(element);
                return;
            }

            foreach (JsonProperty child in element.EnumerateObject())
            {
                Collect(child.Value.Clone(), into, depth + 1);
            }
        }

        static bool IsRecord(JsonElement element) =>
            element.TryGetProperty("id", out JsonElement id)
            && id.ValueKind is JsonValueKind.Number or JsonValueKind.String;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Reads a property as text. Ids are numbers in some client versions and strings in
    /// others, so both are accepted.
    /// </summary>
    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }
}
