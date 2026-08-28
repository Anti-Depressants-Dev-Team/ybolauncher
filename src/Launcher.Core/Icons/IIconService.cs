namespace Launcher.Core.Icons;

/// <summary>
/// Extracts app icons and caches them as PNGs under <c>iconcache\</c>.
/// <para>
/// Extraction is expensive, so it happens once per (source file, modification time, size)
/// and the result is reused on every later launch.
/// </para>
/// </summary>
public interface IIconService
{
    /// <summary>Absolute path of the cache directory.</summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Turns a cache file name stored on an <c>AppEntry</c> into an absolute path,
    /// or null when the file is missing.
    /// </summary>
    string? ResolveCachedPath(string? cacheFileName);

    /// <summary>
    /// Extracts the icon for an executable, shortcut or folder and returns the cache file
    /// name. Returns null when no icon could be produced.
    /// <para>
    /// <b>Must be called on an STA thread</b> - it creates apartment-threaded shell COM
    /// objects. The discovery scan already runs on one; see <c>StaThread</c>.
    /// </para>
    /// </summary>
    string? ExtractFromPath(string sourcePath, int pixelSize);

    /// <summary>
    /// Stores an already-encoded image (packaged app logos are PNGs already) and returns
    /// the cache file name.
    /// </summary>
    Task<string?> SaveEncodedImageAsync(
        string cacheKey,
        byte[] imageBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every cached icon. Returns how many files were removed.</summary>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}
