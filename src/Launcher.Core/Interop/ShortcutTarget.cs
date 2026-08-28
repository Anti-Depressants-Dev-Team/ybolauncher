namespace Launcher.Core.Interop;

/// <summary>What a <c>.lnk</c> file points at, once resolved.</summary>
/// <param name="TargetPath">Target file. Empty for shortcuts that only carry an AUMID.</param>
/// <param name="Arguments">Command line arguments, or null.</param>
/// <param name="WorkingDirectory">Start-in directory, or null.</param>
/// <param name="IconLocation">Explicit icon file the shortcut nominates, or null.</param>
/// <param name="IconIndex">Index within <paramref name="IconLocation"/>.</param>
/// <param name="AppUserModelId">
/// Set when the shortcut launches a packaged app. This is what allows a Start Menu
/// shortcut to be merged with its package catalog entry.
/// </param>
public sealed record ShortcutTarget(
    string TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconLocation,
    int IconIndex,
    string? AppUserModelId);
