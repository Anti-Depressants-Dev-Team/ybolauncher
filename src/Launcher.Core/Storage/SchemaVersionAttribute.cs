namespace Launcher.Core.Storage;

/// <summary>
/// Declares the current on-disk schema version for a persisted document type.
/// <see cref="JsonStorageService"/> compares this against the version found in the file
/// and runs the registered <see cref="IDocumentMigration"/> chain to close any gap.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SchemaVersionAttribute(int version) : Attribute
{
    public int Version { get; } = version;
}
