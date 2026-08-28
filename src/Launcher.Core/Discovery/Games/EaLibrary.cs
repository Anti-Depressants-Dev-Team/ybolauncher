using System.Runtime.Versioning;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// EA app and Origin. Both keep a per-game manifest under ProgramData whose contents are a
/// URL-encoded query string; the offer id in it is what the launch protocol needs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EaLibrary : IGameLibrary
{
    private readonly Func<IEnumerable<string>> _findContentFolders;

    public EaLibrary(Func<IEnumerable<string>>? findContentFolders = null) =>
        _findContentFolders = findContentFolders ?? FindContentFolders;

    public string Name => "EA";

    public IReadOnlyList<GameEntry> Enumerate()
    {
        var games = new List<GameEntry>();

        try
        {
            foreach (string root in _findContentFolders())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string gameFolder in Directory.EnumerateDirectories(root))
                {
                    try
                    {
                        string? manifest = Directory
                            .EnumerateFiles(gameFolder, "*.mfst", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault();

                        if (manifest is null)
                        {
                            continue;
                        }

                        string name = new DirectoryInfo(gameFolder).Name;
                        string? offerId = ParseOfferId(File.ReadAllText(manifest));

                        if (offerId is null || games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        games.Add(new GameEntry
                        {
                            Name = name,
                            LibraryName = "EA",
                            LaunchUri = "origin2://game/launch?offerIds=" + offerId,
                        });
                    }
                    catch (Exception)
                    {
                        // One unreadable manifest costs one game.
                    }
                }
            }
        }
        catch (Exception)
        {
            return games;
        }

        return games;
    }

    private static IEnumerable<string> FindContentFolders()
    {
        string programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        // The EA app kept Origin's layout, so both locations are checked.
        yield return Path.Combine(programData, "Origin", "LocalContent");
        yield return Path.Combine(programData, "EA Desktop", "LocalContent");
    }

    /// <summary>
    /// Pulls the offer id out of a manifest. The file is a single query string such as
    /// <c>?currentstate=kCompleted&amp;id=OFB-EAST%3a12345&amp;...</c>.
    /// </summary>
    public static string? ParseOfferId(string manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest))
        {
            return null;
        }

        foreach (string pair in manifest.Trim().TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            if (!pair.AsSpan(0, separator).Trim().Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = pair[(separator + 1)..].Trim();

            // Manifests carry the id percent-encoded; the launcher wants it decoded.
            return string.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
        }

        return null;
    }
}
