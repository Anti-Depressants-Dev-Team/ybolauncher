using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// itch.io, read from the receipt the app writes inside every install.
/// <para>
/// The app's own index lives in a SQLite database (<c>db\butler.db</c>), which would mean
/// taking a SQLite dependency for one launcher. Every installed game also carries
/// <c>.itch\receipt.json.gz</c> in its own folder, holding the same title and
/// classification, so the install folders are walked instead.
/// </para>
/// </summary>
public sealed class ItchLibrary : IGameLibrary
{
    /// <summary>
    /// itch hosts more than games. Anything in this list is a download, not something that
    /// can be launched.
    /// </summary>
    private static readonly HashSet<string> ExcludedClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets",
        "soundtrack",
        "book",
        "comic",
        "physical_game",
    };

    private readonly Func<string?> _findItchFolder;

    public ItchLibrary(Func<string?>? findItchFolder = null) =>
        _findItchFolder = findItchFolder ?? FindItchFolder;

    public string Name => "itch.io";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        try
        {
            if (_findItchFolder() is not { } itch || !Directory.Exists(itch))
            {
                return [];
            }

            var games = new List<GameEntry>();

            foreach (string root in GetInstallRoots(itch))
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string folder in EnumerateDirectories(root))
                {
                    try
                    {
                        if (ReadInstall(folder) is { } game)
                        {
                            games.Add(game);
                        }
                    }
                    catch (Exception)
                    {
                        // One unreadable install costs one game.
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

    /// <summary>The itch app's data folder.</summary>
    public static string? FindItchFolder()
    {
        try
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return Path.Combine(appData, "itch");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Every folder games may be installed into: the default one, plus anything named in
    /// <c>preferences.json</c>. The user can add install locations on other drives, and
    /// the app has written that list in several shapes over the years, so all of them are
    /// accepted.
    /// </summary>
    private static List<string> GetInstallRoots(string itchFolder)
    {
        var roots = new List<string> { Path.Combine(itchFolder, "apps") };

        try
        {
            string preferences = Path.Combine(itchFolder, "preferences.json");

            if (File.Exists(preferences))
            {
                foreach (string extra in ParseInstallLocations(File.ReadAllText(preferences)))
                {
                    if (!roots.Contains(extra, StringComparer.OrdinalIgnoreCase))
                    {
                        roots.Add(extra);
                    }
                }
            }
        }
        catch (Exception)
        {
            // The default folder alone is still worth walking.
        }

        return roots;
    }

    /// <summary>
    /// Pulls install locations out of the app's preferences. Handles a list of paths, a
    /// list of objects with a <c>path</c>, and a map of id to either.
    /// </summary>
    public static List<string> ParseInstallLocations(string json)
    {
        var paths = new List<string>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return paths;
            }

            foreach (string property in new[] { "installLocations", "downloadLocations", "installLocation" })
            {
                if (document.RootElement.TryGetProperty(property, out JsonElement locations))
                {
                    Collect(locations, paths);
                }
            }
        }
        catch (JsonException)
        {
            // A preferences file we cannot read just means the default location.
        }

        return paths;

        static void Collect(JsonElement element, List<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    Add(element.GetString(), into);
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Collect(item, into);
                    }

                    break;

                case JsonValueKind.Object:
                    if (element.TryGetProperty("path", out JsonElement path))
                    {
                        Add(path.ValueKind == JsonValueKind.String ? path.GetString() : null, into);
                        break;
                    }

                    // A map of location id to location.
                    foreach (JsonProperty child in element.EnumerateObject())
                    {
                        Collect(child.Value, into);
                    }

                    break;

                default:
                    break;
            }
        }

        static void Add(string? path, List<string> into)
        {
            if (!string.IsNullOrWhiteSpace(path) && !into.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                into.Add(path);
            }
        }
    }

    /// <summary>
    /// One install folder, or null when it holds no receipt or nothing launchable.
    /// </summary>
    private static GameEntry? ReadInstall(string folder)
    {
        string? receipt = ReadReceipt(folder);

        if (receipt is null)
        {
            // A folder with no receipt is not an itch install.
            return null;
        }

        return ParseReceipt(receipt, folder);
    }

    /// <summary>
    /// The receipt is gzipped JSON. Some app versions leave a plain copy beside it, so
    /// both are tried.
    /// </summary>
    private static string? ReadReceipt(string folder)
    {
        string gzipped = Path.Combine(folder, ".itch", "receipt.json.gz");

        if (File.Exists(gzipped))
        {
            try
            {
                using FileStream file = File.OpenRead(gzipped);
                using var decompressor = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(decompressor);

                return reader.ReadToEnd();
            }
            catch (Exception)
            {
                // Fall through to the plain copy.
            }
        }

        string plain = Path.Combine(folder, ".itch", "receipt.json");

        try
        {
            return File.Exists(plain) ? File.ReadAllText(plain) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns one receipt into a game, or null when it describes something that is not
    /// launchable.
    /// </summary>
    public static GameEntry? ParseReceipt(string json, string installFolder)
    {
        JsonElement root;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        root.TryGetProperty("game", out JsonElement game);

        if (game.ValueKind == JsonValueKind.Object
            && GetString(game, "classification") is { } classification
            && ExcludedClassifications.Contains(classification))
        {
            return null;
        }

        string name = FirstNonEmpty(
            game.ValueKind == JsonValueKind.Object ? GetString(game, "title") : null,
            root.TryGetProperty("upload", out JsonElement upload) && upload.ValueKind == JsonValueKind.Object
                ? GetString(upload, "displayName")
                : null,
            new DirectoryInfo(installFolder).Name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // itch downloads are DRM-free, so the game runs directly. Without an executable
        // there is nothing to start - a web game runs inside the itch app itself, which
        // this cannot drive.
        string? executable = GameExecutables.FindBest(installFolder, name);

        if (executable is null)
        {
            return null;
        }

        return new GameEntry
        {
            Name = name,
            LibraryName = "itch.io",
            ExecutablePath = executable,
            InstallDirectory = installFolder,
        };
    }

    private static List<string> EnumerateDirectories(string root)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(root)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
