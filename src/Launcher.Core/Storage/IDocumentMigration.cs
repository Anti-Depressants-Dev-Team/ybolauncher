using System.Text.Json.Nodes;

namespace Launcher.Core.Storage;

/// <summary>
/// Upgrades one persisted document from a single schema version to the next.
/// <para>
/// Migrations run on the raw <see cref="JsonObject"/> rather than on a deserialized
/// instance: an old file may not deserialize into the current CLR type at all, which is
/// precisely the case a migration exists to handle.
/// </para>
/// </summary>
public interface IDocumentMigration
{
    /// <summary>The document CLR type this migration applies to.</summary>
    Type DocumentType { get; }

    /// <summary>Schema version this migration reads.</summary>
    int FromVersion { get; }

    /// <summary>Schema version this migration produces. Must be greater than <see cref="FromVersion"/>.</summary>
    int ToVersion { get; }

    /// <summary>
    /// Transforms <paramref name="document"/> in place, or returns a replacement object.
    /// Must not throw for malformed input - return the input unchanged instead.
    /// </summary>
    JsonObject Migrate(JsonObject document);
}
