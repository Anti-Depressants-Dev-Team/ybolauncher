namespace Launcher.Core.Storage;

/// <summary>
/// A persisted document that carries its own schema version.
/// <see cref="JsonStorageService"/> stamps the current version on save and uses the value
/// found on disk to decide which migrations to run on load.
/// </summary>
public interface IVersionedDocument
{
    int SchemaVersion { get; set; }
}
