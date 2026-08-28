namespace Launcher.Core.Storage;

/// <summary>
/// Reads and writes the launcher's JSON documents.
/// <para>
/// Implementations must never throw for ordinary I/O trouble - a missing, locked or
/// corrupt file yields <see langword="null"/> so the caller can fall back to defaults.
/// Losing a user's layout is bad; crashing on startup because of it is worse.
/// </para>
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Loads and migrates a document. Returns <see langword="null"/> when the file does not
    /// exist, cannot be read, or cannot be brought up to the current schema version.
    /// A file that fails to parse is moved aside rather than deleted.
    /// </summary>
    Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Writes a document atomically: serialize to a sibling temp file, flush it to disk,
    /// then swap it into place. A crash mid-write leaves the previous file intact.
    /// </summary>
    Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        where T : class;
}
