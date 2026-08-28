using System.Text.Json.Serialization;
using Launcher.Core.Storage;

namespace Launcher.Core.Models;

/// <summary>One tab in the launcher.</summary>
public sealed class LauncherTab
{
    /// <summary>Reserved id of the Home tab. Home is unique and cannot be recreated.</summary>
    public const string HomeId = "home";

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A monochrome Segoe Fluent Icons glyph from <see cref="TabGlyphs"/>. Null shows no
    /// icon. Anything that is not a Fluent glyph is dropped on load - see
    /// <c>TabService.Normalize</c>.
    /// </summary>
    public string? Glyph { get; set; }

    /// <summary>Accent colour as <c>#RRGGBB</c>, or null to use the system accent.</summary>
    public string? AccentColorHex { get; set; }

    /// <summary>
    /// True only for the Home tab, which always exists, is always first, and cannot be
    /// renamed or deleted.
    /// </summary>
    public bool IsHome { get; set; }

    public SortMode SortMode { get; set; } = SortMode.Manual;

    public ViewMode ViewMode { get; set; } = ViewMode.MediumGrid;

    /// <summary>
    /// Multiplier on the tile size for this tab, driven by the size slider.
    /// Clamped to <see cref="MinTileScale"/>..<see cref="MaxTileScale"/> when applied.
    /// </summary>
    public double TileScale { get; set; } = 1.0;

    public const double MinTileScale = 0.75;

    public const double MaxTileScale = 1.6;

    /// <summary>
    /// For a custom tab this is the membership list, in display order.
    /// <para>
    /// For Home it is <em>order only</em>: Home always shows every discovered app, so any
    /// entry missing from this list is appended rather than excluded. That is what lets
    /// Home be manually reordered without a newly installed app going missing.
    /// </para>
    /// </summary>
    public List<string> EntryIds { get; set; } = [];

    [JsonIgnore]
    public bool CanBeRenamed => !IsHome;

    [JsonIgnore]
    public bool CanBeDeleted => !IsHome;

    /// <summary>Creates the Home tab. Only <see cref="TabService"/> should need this.</summary>
    public static LauncherTab CreateHome() => new()
    {
        Id = HomeId,
        Name = "Home",
        IsHome = true,

        // Home starts alphabetical; the first manual drag switches it to Manual.
        SortMode = SortMode.Alphabetical,

        Glyph = TabGlyphs.Home,
    };
}

/// <summary>Everything persisted to <c>tabs.json</c>: the tab list and its order.</summary>
[SchemaVersion(1)]
public sealed class TabLayout : IVersionedDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<LauncherTab> Tabs { get; set; } = [];
}
