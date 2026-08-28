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

    /// <summary>An emoji, or a Segoe Fluent Icons glyph. Null shows no icon.</summary>
    public string? Glyph { get; set; }

    /// <summary>Accent colour as <c>#RRGGBB</c>, or null to use the system accent.</summary>
    public string? AccentColorHex { get; set; }

    /// <summary>
    /// True only for the Home tab, which always exists, is always first, and cannot be
    /// renamed or deleted.
    /// </summary>
    public bool IsHome { get; set; }

    public SortMode SortMode { get; set; } = SortMode.Manual;

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

        // An emoji rather than a Segoe Fluent glyph, so one font renders every tab icon.
        // Custom tabs use emoji, and mixing the two would need per-tab font switching.
        Glyph = "\U0001F3E0",
    };
}

/// <summary>Everything persisted to <c>tabs.json</c>: the tab list and its order.</summary>
[SchemaVersion(1)]
public sealed class TabLayout : IVersionedDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<LauncherTab> Tabs { get; set; } = [];
}
