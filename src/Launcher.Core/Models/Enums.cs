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
