namespace Launcher.Core.Storage;

/// <summary>Outcome of an import.</summary>
/// <param name="Succeeded">True when the archive replaced the current configuration.</param>
/// <param name="Error">Human-readable reason when it did not.</param>
/// <param name="BackupPath">
/// Where the previous configuration was saved before being replaced, so a bad import can
/// be undone by importing the backup.
/// </param>
public sealed record ImportResult(bool Succeeded, string? Error, string? BackupPath)
{
    public static ImportResult Failed(string error) => new(false, error, null);

    public static ImportResult Success(string backupPath) => new(true, null, backupPath);
}

/// <summary>
/// Exports and imports the whole configuration - settings, tabs, the app catalog and the
/// icon cache - as a single zip.
/// </summary>
public interface IConfigArchiveService
{
    /// <summary>Writes the current configuration to a zip. Returns false on failure.</summary>
    Task<bool> ExportAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the current configuration from a zip, after backing up what is there.
    /// The caller must reload every service afterwards.
    /// </summary>
    Task<ImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}
