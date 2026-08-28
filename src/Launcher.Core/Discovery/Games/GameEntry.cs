namespace Launcher.Core.Discovery.Games;

/// <summary>One installed game, as reported by a launcher's own bookkeeping.</summary>
public sealed record GameEntry
{
    public required string Name { get; init; }

    /// <summary>Which launcher it came from, e.g. "Steam". Shown in the entry's detail line.</summary>
    public required string LibraryName { get; init; }

    /// <summary>
    /// Protocol URI that starts the game through its launcher, e.g.
    /// <c>steam://rungameid/440</c>. Null when the game is started by running its
    /// executable directly, which is how DRM-free libraries work.
    /// </summary>
    public string? LaunchUri { get; init; }

    /// <summary>
    /// The game's executable. Used for the icon and for "open file location" even when the
    /// game is launched through a protocol.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Command line for <see cref="ExecutablePath"/>. Riot runs every game through one
    /// shared client executable, so the product is a switch rather than a path.
    /// </summary>
    public string? Arguments { get; init; }

    public string? InstallDirectory { get; init; }

    /// <summary>
    /// An icon the launcher already cached, when it has one. Steam keeps per-game icons;
    /// most others do not, so the executable is used instead.
    /// </summary>
    public string? IconPath { get; init; }
}

/// <summary>
/// One game launcher's library. Implementations report nothing at all when that launcher
/// is not installed, which is the normal case for most of them on any given machine.
/// </summary>
public interface IGameLibrary
{
    /// <summary>Display name of the launcher.</summary>
    string Name { get; }

    /// <summary>
    /// Installed games. Must never throw: a launcher with a corrupt or unexpected data
    /// file should yield fewer games, not break the scan.
    /// </summary>
    IReadOnlyList<GameEntry> Enumerate();
}
