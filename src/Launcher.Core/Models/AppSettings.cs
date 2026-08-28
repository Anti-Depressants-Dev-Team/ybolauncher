using System.Text.Json.Serialization;
using Launcher.Core.Storage;

namespace Launcher.Core.Models;

/// <summary>
/// Everything persisted to <c>settings.json</c>.
/// <para>
/// New settings are plain new properties with a default; the migration machinery handles
/// them appearing in an older file without needing a schema bump.
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

    /// <summary>Scan Steam, Epic, GOG, Ubisoft Connect, EA and Battle.net libraries.</summary>
    public bool ScanGameLaunchers { get; set; } = true;

    /// <summary>Look for a new release when the launcher starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

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

    /// <summary>View mode a newly created tab starts with.</summary>
    public ViewMode DefaultViewMode { get; set; } = ViewMode.MediumGrid;

    /// <summary>Tile size multiplier a newly created tab starts with.</summary>
    public double DefaultTileScale { get; set; } = 1.0;

    /// <summary>System-wide hotkey that summons and hides the launcher.</summary>
    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.CreateDefault();

    /// <summary>
    /// Off by default. A global hotkey takes a key combination away from every other app,
    /// so it is opt-in rather than something that happens on first run.
    /// </summary>
    public bool HotkeyEnabled { get; set; }

    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Hide the launcher once an app has been started from it.</summary>
    public bool HideAfterLaunch { get; set; }

    /// <summary>Start into the tray without showing the window. Used with "start with Windows".</summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Restores every preference to its default, in place.
    /// <para>
    /// Window geometry and the last active tab are deliberately kept: they are restored
    /// state rather than preferences, and having the window jump across the screen is not
    /// what "reset settings" should mean.
    /// </para>
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new AppSettings();

        Theme = defaults.Theme;
        Backdrop = defaults.Backdrop;
        ScanStartMenu = defaults.ScanStartMenu;
        ScanPackagedApps = defaults.ScanPackagedApps;
        ScanGameLaunchers = defaults.ScanGameLaunchers;
        CheckForUpdates = defaults.CheckForUpdates;
        ShowFilteredEntries = defaults.ShowFilteredEntries;
        ShowHiddenEntries = defaults.ShowHiddenEntries;
        SearchCurrentTabOnly = defaults.SearchCurrentTabOnly;
        DefaultViewMode = defaults.DefaultViewMode;
        DefaultTileScale = defaults.DefaultTileScale;
        Hotkey = defaults.Hotkey;
        HotkeyEnabled = defaults.HotkeyEnabled;
        MinimizeToTray = defaults.MinimizeToTray;
        HideAfterLaunch = defaults.HideAfterLaunch;
        StartMinimized = defaults.StartMinimized;
    }

    /// <summary>Creates an independent copy, used to diff or roll back pending edits.</summary>
    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Theme = Theme,
        Backdrop = Backdrop,
        LastActiveTabId = LastActiveTabId,
        ScanStartMenu = ScanStartMenu,
        ScanPackagedApps = ScanPackagedApps,
        ScanGameLaunchers = ScanGameLaunchers,
        CheckForUpdates = CheckForUpdates,
        ShowFilteredEntries = ShowFilteredEntries,
        ShowHiddenEntries = ShowHiddenEntries,
        SearchCurrentTabOnly = SearchCurrentTabOnly,
        DefaultViewMode = DefaultViewMode,
        DefaultTileScale = DefaultTileScale,
        Hotkey = Hotkey.Clone(),
        HotkeyEnabled = HotkeyEnabled,
        MinimizeToTray = MinimizeToTray,
        HideAfterLaunch = HideAfterLaunch,
        StartMinimized = StartMinimized,
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
