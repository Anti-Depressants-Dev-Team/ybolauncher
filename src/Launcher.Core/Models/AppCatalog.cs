using Launcher.Core.Storage;

namespace Launcher.Core.Models;

/// <summary>Everything persisted to <c>apps.json</c>.</summary>
[SchemaVersion(1)]
public sealed class AppCatalog : IVersionedDocument
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset? LastScanUtc { get; set; }

    /// <summary>
    /// Build that produced this catalog. Discovery rules change between releases - what
    /// counts as a duplicate, what counts as a game - so a catalog written by a different
    /// build is rescanned rather than shown as it was.
    /// </summary>
    public string? BuiltByVersion { get; set; }

    public List<AppEntry> Entries { get; set; } = [];
}
