using System.Text.Json.Serialization;

namespace Launcher.Core.Models;

/// <summary>
/// One launchable app. Discovery produces these; the user's edits (rename, custom icon,
/// hide, launch stats) are layered on top and survive a rescan because <see cref="Id"/>
/// is derived deterministically from what the app <em>is</em>, not from scan order.
/// </summary>
public sealed class AppEntry
{
    /// <summary>Stable identity, derived from <see cref="MergeKey"/>. See <c>AppIdentity</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The key two discovered entries must share to be considered the same app.
    /// Persisted so a rescan can re-link to user edits without recomputing from scratch.
    /// </summary>
    public string MergeKey { get; set; } = string.Empty;

    /// <summary>Name shown in the UI. Equals <see cref="OriginalName"/> until the user renames it.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Name as discovered. Never overwritten by a rename, so it can be restored.</summary>
    public string OriginalName { get; set; } = string.Empty;

    public AppSource Source { get; set; }

    public LaunchKind LaunchKind { get; set; }

    /// <summary>Executable or document path for <see cref="LaunchKind.Executable"/>.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Protocol URI for <see cref="LaunchKind.Uri"/>.</summary>
    public string? LaunchUri { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    /// <summary>The .lnk this came from, if any. Used for "Open file location".</summary>
    public string? ShortcutPath { get; set; }

    /// <summary>Application User Model ID, for packaged apps.</summary>
    public string? AppUserModelId { get; set; }

    public string? PackageFamilyName { get; set; }

    /// <summary>File name (not full path) inside the icon cache directory.</summary>
    public string? IconCacheFile { get; set; }

    /// <summary>User-chosen icon, which overrides <see cref="IconCacheFile"/>.</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>Hidden from Home by the user.</summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// True when the junk filter rejected this entry. Kept in the catalog rather than
    /// dropped, so "show filtered entries" can reveal it without a rescan.
    /// </summary>
    public bool IsFiltered { get; set; }

    public FilterReason FilterReason { get; set; }

    public bool IsFavorite { get; set; }

    public int LaunchCount { get; set; }

    public DateTimeOffset? LastLaunchedUtc { get; set; }

    /// <summary>True when this entry should appear on Home.</summary>
    [JsonIgnore]
    public bool IsVisibleOnHome => !IsHidden && !IsFiltered;

    /// <summary>Copies discovery-owned fields from a fresh scan, preserving user edits.</summary>
    public void UpdateFromScan(AppEntry scanned)
    {
        ArgumentNullException.ThrowIfNull(scanned);

        // A rename is preserved: only follow the scan when the user never renamed it.
        bool wasRenamed = !string.Equals(DisplayName, OriginalName, StringComparison.Ordinal);

        OriginalName = scanned.OriginalName;
        if (!wasRenamed)
        {
            DisplayName = scanned.OriginalName;
        }

        Source = scanned.Source;
        LaunchKind = scanned.LaunchKind;
        TargetPath = scanned.TargetPath;
        LaunchUri = scanned.LaunchUri;
        Arguments = scanned.Arguments;
        WorkingDirectory = scanned.WorkingDirectory;
        ShortcutPath = scanned.ShortcutPath;
        AppUserModelId = scanned.AppUserModelId;
        PackageFamilyName = scanned.PackageFamilyName;
        IsFiltered = scanned.IsFiltered;
        FilterReason = scanned.FilterReason;

        // Only adopt a freshly extracted icon; never clobber a user-chosen one.
        if (scanned.IconCacheFile is not null)
        {
            IconCacheFile = scanned.IconCacheFile;
        }
    }
}
