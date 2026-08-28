namespace Launcher.Core.Models;

/// <summary>Requested application theme. <see cref="System"/> follows the Windows setting.</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Which system backdrop the main window uses.</summary>
public enum BackdropKind
{
    Mica = 0,
    MicaAlt = 1,
}

/// <summary>How entries are laid out inside a tab.</summary>
public enum ViewMode
{
    LargeGrid = 0,
    MediumGrid = 1,
    CompactList = 2,
}

/// <summary>Ordering applied to the entries inside a tab.</summary>
public enum SortMode
{
    /// <summary>User-defined drag order.</summary>
    Manual = 0,
    Alphabetical = 1,
    MostUsed = 2,
    RecentlyUsed = 3,
}

/// <summary>Where a discovered entry came from.</summary>
public enum AppSource
{
    StartMenu = 0,
    Packaged = 1,
    Steam = 2,
    Epic = 3,
    XboxGamePass = 4,

    /// <summary>A game found through a launcher such as Steam, Epic, GOG or Ubisoft Connect.</summary>
    GameLauncher = 6,

    /// <summary>Dragged in from Explorer by the user.</summary>
    UserAdded = 5,
}

/// <summary>How an entry is started.</summary>
public enum LaunchKind
{
    /// <summary>ShellExecute on a file system path.</summary>
    Executable = 0,

    /// <summary>Resolved through the package catalog and started with AppListEntry.LaunchAsync.</summary>
    PackagedApp = 1,

    /// <summary>A protocol URI such as steam://rungameid/440.</summary>
    Uri = 2,
}

/// <summary>Why the junk filter rejected an entry. <see cref="None"/> means it was kept.</summary>
public enum FilterReason
{
    None = 0,
    Uninstaller = 1,
    Documentation = 2,
    WebLink = 3,
    BrokenTarget = 4,
    NoLaunchTarget = 5,
    SystemComponent = 6,
}
