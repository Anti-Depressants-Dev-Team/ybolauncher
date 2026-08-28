using System.Runtime.Versioning;
using System.Text.Json;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Epic Games Store, read from the launcher's per-game manifests in ProgramData. Each
/// <c>.item</c> file is JSON describing one installed title.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EpicLibrary : IGameLibrary
{
    private readonly Func<string?> _findManifestFolder;

    public EpicLibrary(Func<string?>? findManifestFolder = null) =>
        _findManifestFolder = findManifestFolder ?? FindManifestFolder;

    public string Name => "Epic Games";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        try
        {
            if (_findManifestFolder() is not { } folder || !Directory.Exists(folder))
            {
                return [];
            }

            var games = new List<GameEntry>();

            foreach (string manifest in Directory.EnumerateFiles(folder, "*.item"))
            {
                try
                {
                    if (ParseManifest(File.ReadAllText(manifest)) is { } game)
                    {
                        games.Add(game);
                    }
                }
                catch (Exception)
                {
                    // One bad manifest costs one game.
                }
            }

            return games;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string? FindManifestFolder()
    {
        try
        {
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns one manifest into a game, or null when it is not a finished game install.
    /// </summary>
    public static GameEntry? ParseManifest(string json)
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

        string? name = GetString(root, "DisplayName");
        string? appName = GetString(root, "AppName");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        // Epic writes a manifest as soon as a download starts.
        if (root.TryGetProperty("bIsIncompleteInstall", out JsonElement incomplete)
            && incomplete.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        // The launcher also tracks engine installs and plugins in the same folder.
        if (!IsGame(root))
        {
            return null;
        }

        string? installLocation = GetString(root, "InstallLocation");
        string? executable = GetString(root, "LaunchExecutable");

        string? executablePath = !string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(executable)
            ? Path.Combine(installLocation, executable.Replace('/', Path.DirectorySeparatorChar))
            : null;

        return new GameEntry
        {
            Name = name,
            LibraryName = "Epic Games",
            LaunchUri = BuildLaunchUri(root, appName),
            InstallDirectory = installLocation,
            ExecutablePath = executablePath,
        };
    }

    /// <summary>
    /// The fully qualified form - namespace, catalog item and app name - is what the
    /// launcher's own shortcuts use. The short form is a fallback for older manifests that
    /// do not carry the catalog fields.
    /// </summary>
    private static string BuildLaunchUri(JsonElement root, string appName)
    {
        string? ns = GetString(root, "CatalogNamespace");
        string? itemId = GetString(root, "CatalogItemId");

        string target = !string.IsNullOrWhiteSpace(ns) && !string.IsNullOrWhiteSpace(itemId)
            ? string.Concat(ns, "%3A", itemId, "%3A", appName)
            : appName;

        return "com.epicgames.launcher://apps/" + target + "?action=launch&silent=true";
    }

    /// <summary>
    /// Epic tags each manifest with categories. When the field is present it must include
    /// "games"; when it is absent - older manifests - the entry is accepted.
    /// </summary>
    private static bool IsGame(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out JsonElement categories)
            || categories.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (JsonElement category in categories.EnumerateArray())
        {
            if (category.ValueKind == JsonValueKind.String
                && string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
