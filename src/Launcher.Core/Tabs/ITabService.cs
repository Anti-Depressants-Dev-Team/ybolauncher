using Launcher.Core.Models;

namespace Launcher.Core.Tabs;

/// <summary>
/// Owns the tab list and its persistence.
/// <para>
/// Every mutation goes through here so the Home invariants hold in one place: Home always
/// exists, is always first, and is never renamed, moved or deleted.
/// </para>
/// </summary>
public interface ITabService
{
    /// <summary>All tabs in display order. Index 0 is always Home.</summary>
    IReadOnlyList<LauncherTab> Tabs { get; }

    LauncherTab Home { get; }

    /// <summary>Raised after the tab list or a tab's contents change.</summary>
    event EventHandler? TabsChanged;

    /// <summary>Reads tabs.json, creating a default Home tab when there is nothing to read.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task<LauncherTab> CreateTabAsync(
        string name,
        string? glyph = null,
        string? accentColorHex = null,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a tab. Ignored for Home.</summary>
    Task RenameTabAsync(string tabId, string name, CancellationToken cancellationToken = default);

    /// <summary>Updates the glyph and accent colour. Allowed for Home.</summary>
    Task SetAppearanceAsync(
        string tabId,
        string? glyph,
        string? accentColorHex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tab. Returns false for Home or an unknown id. Never touches the apps
    /// themselves - only this tab's membership list goes away.
    /// </summary>
    Task<bool> DeleteTabAsync(string tabId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a tab to a new position. Home is pinned at index 0, so a target of 0 for any
    /// other tab becomes 1, and Home itself cannot be moved.
    /// </summary>
    Task MoveTabAsync(string tabId, int targetIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a whole new tab order, used after the strip is rearranged by dragging.
    /// Home is forced back to index 0 regardless of where it was dropped, and any tab the
    /// caller omitted keeps its place at the end rather than being lost.
    /// </summary>
    Task ReorderAsync(IReadOnlyList<string> orderedTabIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds entries to a tab, skipping ones already there. Adding to Home is a no-op:
    /// Home already contains everything.
    /// </summary>
    Task AddEntriesAsync(
        string tabId,
        IEnumerable<string> entryIds,
        int? insertIndex = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes entries from a tab's membership. Ignored for Home, where an app is removed
    /// by hiding it rather than by un-listing it.
    /// </summary>
    Task RemoveEntriesAsync(
        string tabId,
        IEnumerable<string> entryIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a tab's order after a manual drag, switching the tab to
    /// <see cref="SortMode.Manual"/> so the new order actually sticks.
    /// </summary>
    Task SetOrderAsync(
        string tabId,
        IReadOnlyList<string> entryIds,
        CancellationToken cancellationToken = default);

    /// <summary>Whether an entry is a member of the given custom tab.</summary>
    bool Contains(string tabId, string entryId);

    /// <summary>
    /// Drops references to entries that are no longer in the catalog - an uninstalled app
    /// should not leave a hole in a tab. Returns true when anything was removed.
    /// </summary>
    Task<bool> PruneAsync(IReadOnlySet<string> knownEntryIds, CancellationToken cancellationToken = default);
}
