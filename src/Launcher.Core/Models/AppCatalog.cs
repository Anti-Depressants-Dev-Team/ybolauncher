using Launcher.Core.Storage;

namespace Launcher.Core.Models;

/// <summary>Everything persisted to <c>apps.json</c>.</summary>
[SchemaVersion(1)]
public sealed class AppCatalog : IVersionedDocument
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset? LastScanUtc { get; set; }

    public List<AppEntry> Entries { get; set; } = [];
}
