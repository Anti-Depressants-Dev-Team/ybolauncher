using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core.Icons;

/// <summary>
/// Builds the cache file name for an icon.
/// <para>
/// The key folds in the source file's last-write time, so replacing an executable
/// (an app update) naturally produces a new cache entry instead of serving a stale icon.
/// </para>
/// </summary>
public static class IconCacheKey
{
    /// <summary>
    /// Bumped when the way an icon is produced changes, so every cached file is rebuilt
    /// rather than serving one made the old way. v2 crops the transparent padding.
    /// </summary>
    private const string CacheVersion = "v2";

    /// <summary>Cache file name for an icon extracted from a file on disk.</summary>
    public static string ForFile(string sourcePath, DateTime lastWriteUtc, int pixelSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string material = string.Create(
            CultureInfo.InvariantCulture,
            $"file|{CacheVersion}|{sourcePath.ToLowerInvariant()}|{lastWriteUtc.Ticks}|{pixelSize}");

        return Hash(material);
    }

    /// <summary>
    /// Cache file name for a packaged app logo. Keyed on the package version so an app
    /// update refreshes the icon.
    /// </summary>
    public static string ForPackagedApp(string appUserModelId, string packageVersion, int pixelSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);

        string material = string.Create(
            CultureInfo.InvariantCulture,
            $"pkg|{CacheVersion}|{appUserModelId.ToLowerInvariant()}|{packageVersion}|{pixelSize}");

        return Hash(material);
    }

    private static string Hash(string material)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant() + ".png";
    }
}
