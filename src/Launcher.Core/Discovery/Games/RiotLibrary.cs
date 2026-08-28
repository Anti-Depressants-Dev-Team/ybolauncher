using System.Text.Json;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Riot Games, read from the metadata the Riot Client keeps in ProgramData.
/// <para>
/// Riot installs one client for every game: <c>RiotClientServices.exe</c> starts a product
/// by switch rather than each game having its own launchable executable. The client's path
/// comes from <c>RiotClientInstalls.json</c> and the installed products are the folders
/// under <c>Metadata</c>, each named <c>&lt;product&gt;.&lt;patchline&gt;</c>.
/// </para>
/// </summary>
public sealed class RiotLibrary : IGameLibrary
{
    /// <summary>
    /// Product ids as Riot writes them, with the name to show and the executable to take
    /// the icon from. The client is the same file for every game, so its icon would
    /// otherwise be on every tile.
    /// </summary>
    private static readonly (string Product, string Title, string IconExecutable)[] KnownProducts =
    [
        ("league_of_legends", "League of Legends", "LeagueClient.exe"),
        ("valorant", "VALORANT", "VALORANT.exe"),
        ("bacon", "Legends of Runeterra", "LoR.exe"),
    ];

    private readonly Func<string?> _findRiotFolder;

    public RiotLibrary(Func<string?>? findRiotFolder = null) =>
        _findRiotFolder = findRiotFolder ?? FindRiotFolder;

    public string Name => "Riot Games";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        try
        {
            if (_findRiotFolder() is not { } riot || !Directory.Exists(riot))
            {
                return [];
            }

            string? client = FindClient(riot);

            if (client is null)
            {
                return [];
            }

            string metadata = Path.Combine(riot, "Metadata");

            if (!Directory.Exists(metadata))
            {
                return [];
            }

            var games = new List<GameEntry>();

            foreach (string folder in Directory.EnumerateDirectories(metadata))
            {
                try
                {
                    string id = new DirectoryInfo(folder).Name;
                    GameEntry? game = BuildGame(id, ReadInstallPath(folder, id), client);

                    if (game is not null)
                    {
                        games.Add(game);
                    }
                }
                catch (Exception)
                {
                    // One unreadable product costs one game.
                }
            }

            return games;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Riot's shared data folder.</summary>
    public static string? FindRiotFolder()
    {
        try
        {
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return Path.Combine(programData, "Riot Games");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindClient(string riotFolder)
    {
        try
        {
            string installs = Path.Combine(riotFolder, "RiotClientInstalls.json");

            return File.Exists(installs) ? ParseClientPath(File.ReadAllText(installs)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the client path out of <c>RiotClientInstalls.json</c>. The default entry is
    /// preferred; the live and beta ones are fallbacks for an older file.
    /// </summary>
    public static string? ParseClientPath(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (string property in new[] { "rc_default", "rc_live", "rc_beta" })
            {
                if (document.RootElement.TryGetProperty(property, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } path)
                {
                    return path;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadInstallPath(string metadataFolder, string id)
    {
        try
        {
            string settings = Path.Combine(metadataFolder, id + ".product_settings.yaml");

            return File.Exists(settings) ? ParseInstallPath(File.ReadAllText(settings)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Pulls the install folder out of a product settings file. Only one key is wanted, so
    /// the line is read directly rather than taking a YAML dependency for it.
    /// </summary>
    public static string? ParseInstallPath(string yaml)
    {
        foreach (string line in yaml.Split('\n'))
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("product_install_full_path:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = trimmed["product_install_full_path:".Length..].Trim().Trim('"', '\'');

            if (value.Length > 0)
            {
                return value.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        return null;
    }

    /// <summary>
    /// Turns one metadata folder into a game, or null when it is not a playable product.
    /// </summary>
    /// <param name="id">Folder name, <c>&lt;product&gt;.&lt;patchline&gt;</c>.</param>
    public static GameEntry? BuildGame(string id, string? installPath, string clientPath)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(clientPath))
        {
            return null;
        }

        int separator = id.IndexOf('.', StringComparison.Ordinal);
        string product = separator > 0 ? id[..separator] : id;
        string patchline = separator > 0 ? id[(separator + 1)..] : "live";

        if (product.Length == 0)
        {
            return null;
        }

        // The client is listed alongside the games it installs.
        if (product.Equals("riot_client", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        (string _, string title, string iconExecutable) = KnownProducts.FirstOrDefault(
            p => p.Product.Equals(product, StringComparison.OrdinalIgnoreCase));

        // A product this build has never heard of still gets a tile.
        title ??= product.Replace('_', ' ');

        // Anything but the live patchline is a separate install of the same game.
        string name = patchline.Equals("live", StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title} ({patchline.ToUpperInvariant()})";

        return new GameEntry
        {
            Name = name,
            LibraryName = "Riot Games",

            // Every Riot game starts through the one client, which is what handles the
            // patcher and sign-in.
            ExecutablePath = clientPath,
            Arguments = $"--launch-product={product} --launch-patchline={patchline}",
            InstallDirectory = installPath,
            IconPath = FindIcon(installPath, iconExecutable, title),
        };
    }

    /// <summary>
    /// The game's own executable, used for the icon only. Without it every Riot tile would
    /// show the client's icon.
    /// </summary>
    private static string? FindIcon(string? installPath, string? iconExecutable, string title)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(iconExecutable))
        {
            try
            {
                string direct = Path.Combine(installPath, iconExecutable);

                if (File.Exists(direct))
                {
                    return direct;
                }

                // VALORANT keeps its executable in a patchline folder below the install.
                foreach (string child in Directory.EnumerateDirectories(installPath))
                {
                    string nested = Path.Combine(child, iconExecutable);

                    if (File.Exists(nested))
                    {
                        return nested;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to the generic search.
            }
        }

        return GameExecutables.FindBest(installPath, title);
    }
}
