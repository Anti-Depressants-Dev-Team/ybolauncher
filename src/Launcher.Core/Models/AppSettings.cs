using System.Text.Json.Serialization;
using Launcher.Core.Storage;

namespace Launcher.Core.Models;

/// <summary>
/// Everything persisted to <c>settings.json</c>.
/// <para>
/// Only the settings the shell actually consumes today are present. Discovery-source
/// toggles, hotkey binding and tray behaviour arrive with the phases that implement them
/// (see SPEC.md) - each addition is a plain new property with a default, which the
/// migration machinery handles without a version bump.
/// </para>
/// </summary>
[SchemaVersion(1)]
public sealed class AppSettings : IVersionedDocument
{
    public int SchemaVersion { get; set; } = 1;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    public WindowPlacement Window { get; set; } = new();

    /// <summary>Id of the tab selected when the app was last closed. Null selects Home.</summary>
    public string? LastActiveTabId { get; set; }

    /// <summary>Scan the machine and user Start Menu folders.</summary>
    public bool ScanStartMenu { get; set; } = true;

    /// <summary>Scan the Store / MSIX package catalog.</summary>
    public bool ScanPackagedApps { get; set; } = true;

    /// <summary>
    /// Show entries the junk filter rejected - uninstallers, documentation links, broken
    /// shortcuts. They stay in the catalog either way, so this needs no rescan.
    /// </summary>
    public bool ShowFilteredEntries { get; set; }

    /// <summary>
    /// Show entries the user hid from Home. Hiding must stay reversible, so this is what
    /// makes them visible again for un-hiding.
    /// </summary>
    public bool ShowHiddenEntries { get; set; }

    /// <summary>
    /// Limit search to the selected tab. Off by default: SPEC.md says search covers all
    /// tabs unless the user narrows it.
    /// </summary>
    public bool SearchCurrentTabOnly { get; set; }

    /// <summary>Creates an independent copy, used to diff or roll back pending edits.</summary>
    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Theme = Theme,
        Backdrop = Backdrop,
        LastActiveTabId = LastActiveTabId,
        ScanStartMenu = ScanStartMenu,
        ScanPackagedApps = ScanPackagedApps,
        ShowFilteredEntries = ShowFilteredEntries,
        ShowHiddenEntries = ShowHiddenEntries,
        SearchCurrentTabOnly = SearchCurrentTabOnly,
        Window = Window.Clone(),
    };
}

/// <summary>Restored window geometry. Zero width or height means "use the default size".</summary>
public sealed class WindowPlacement
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int Left { get; set; }

    public int Top { get; set; }

    public bool IsMaximized { get; set; }

    /// <summary>True when a real geometry has been recorded at least once.</summary>
    [JsonIgnore]
    public bool HasValue => Width > 0 && Height > 0;

    public WindowPlacement Clone() => new()
    {
        Width = Width,
        Height = Height,
        Left = Left,
        Top = Top,
        IsMaximized = IsMaximized,
    };
}
