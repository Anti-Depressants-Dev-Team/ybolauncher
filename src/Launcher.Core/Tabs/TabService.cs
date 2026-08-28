using Launcher.Core.Models;
using Launcher.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Tabs;

/// <inheritdoc cref="ITabService"/>
public sealed class TabService : ITabService
{
    private readonly IStorageService _storage;
    private readonly StoragePaths _paths;
    private readonly ILogger<TabService> _logger;

    private List<LauncherTab> _tabs = [LauncherTab.CreateHome()];

    public TabService(IStorageService storage, StoragePaths paths, ILogger<TabService>? logger = null)
    {
        _storage = storage;
        _paths = paths;
        _logger = logger ?? NullLogger<TabService>.Instance;
    }

    public IReadOnlyList<LauncherTab> Tabs => _tabs;

    public LauncherTab Home => _tabs[0];

    public event EventHandler? TabsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        TabLayout? stored = await _storage
            .LoadAsync<TabLayout>(_paths.TabsFile, cancellationToken)
            .ConfigureAwait(false);

        _tabs = Normalize(stored?.Tabs);

        _logger.LogInformation("Loaded {Count} tabs.", _tabs.Count);
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enforces the Home invariants on whatever came off disk: exactly one Home, at index
    /// 0, with no duplicate ids. A hand-edited or partially-written tabs.json must still
    /// produce a usable tab list.
    /// </summary>
    private static List<LauncherTab> Normalize(List<LauncherTab>? stored)
    {
        var result = new List<LauncherTab>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        LauncherTab home = stored?.FirstOrDefault(t => t.IsHome || t.Id == LauncherTab.HomeId)
            ?? LauncherTab.CreateHome();

        // Repair a Home tab that was edited into something else.
        home.Id = LauncherTab.HomeId;
        home.IsHome = true;
        if (string.IsNullOrWhiteSpace(home.Name))
        {
            home.Name = "Home";
        }

        result.Add(home);
        seenIds.Add(home.Id);

        foreach (LauncherTab tab in stored ?? [])
        {
            if (ReferenceEquals(tab, home) || tab.IsHome || tab.Id == LauncherTab.HomeId)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(tab.Id) || !seenIds.Add(tab.Id))
            {
                // A blank or duplicate id would make the tab unaddressable; re-key it.
                tab.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(tab.Id);
            }

            tab.IsHome = false;
            result.Add(tab);
        }

        return result;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var document = new TabLayout { Tabs = _tabs };

        try
        {
            await _storage.SaveAsync(_paths.TabsFile, document, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The in-memory layout is still good; losing the write is not worth a crash.
            _logger.LogError(ex, "Could not write {Path}.", _paths.TabsFile);
        }
    }

    public async Task<LauncherTab> CreateTabAsync(
        string name,
        string? glyph = null,
        string? accentColorHex = null,
        CancellationToken cancellationToken = default)
    {
        var tab = new LauncherTab
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "New tab" : name.Trim(),
            Glyph = NullIfBlank(glyph),
            AccentColorHex = NullIfBlank(accentColorHex),
            IsHome = false,
            SortMode = SortMode.Manual,
        };

        _tabs.Add(tab);

        await CommitAsync(cancellationToken).ConfigureAwait(false);
        return tab;
    }

    public async Task RenameTabAsync(string tabId, string name, CancellationToken cancellationToken = default)
    {
        LauncherTab? tab = Find(tabId);

        if (tab is null || !tab.CanBeRenamed || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        tab.Name = name.Trim();
        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAppearanceAsync(
        string tabId,
        string? glyph,
        string? accentColorHex,
        CancellationToken cancellationToken = default)
    {
        LauncherTab? tab = Find(tabId);
        if (tab is null)
        {
            return;
        }

        tab.Glyph = NullIfBlank(glyph);
        tab.AccentColorHex = NullIfBlank(accentColorHex);

        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        LauncherTab? tab = Find(tabId);

        if (tab is null || !tab.CanBeDeleted)
        {
            return false;
        }

        // Only the membership list goes; the apps themselves are untouched and stay on Home.
        _tabs.Remove(tab);

        await CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task MoveTabAsync(string tabId, int targetIndex, CancellationToken cancellationToken = default)
    {
        LauncherTab? tab = Find(tabId);

        if (tab is null || tab.IsHome)
        {
            return;
        }

        int current = _tabs.IndexOf(tab);

        // Home owns index 0 permanently, so everything else lands at 1 or later.
        int clamped = Math.Clamp(targetIndex, 1, _tabs.Count - 1);

        if (current == clamped)
        {
            return;
        }

        _tabs.RemoveAt(current);
        _tabs.Insert(clamped, tab);

        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderAsync(
        IReadOnlyList<string> orderedTabIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedTabIds);

        var reordered = new List<LauncherTab>(_tabs.Count);

        foreach (string id in orderedTabIds)
        {
            LauncherTab? tab = Find(id);
            if (tab is not null && !reordered.Contains(tab))
            {
                reordered.Add(tab);
            }
        }

        // Anything the caller left out keeps its place rather than disappearing.
        foreach (LauncherTab tab in _tabs)
        {
            if (!reordered.Contains(tab))
            {
                reordered.Add(tab);
            }
        }

        // Home is pinned, wherever the user tried to drop it.
        LauncherTab home = reordered.First(t => t.IsHome);
        reordered.Remove(home);
        reordered.Insert(0, home);

        if (reordered.SequenceEqual(_tabs))
        {
            return;
        }

        _tabs = reordered;
        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddEntriesAsync(
        string tabId,
        IEnumerable<string> entryIds,
        int? insertIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        LauncherTab? tab = Find(tabId);

        // Home already contains every app; adding to it would be meaningless.
        if (tab is null || tab.IsHome)
        {
            return;
        }

        var additions = entryIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !tab.EntryIds.Contains(id, StringComparer.Ordinal))
            .ToList();

        if (additions.Count == 0)
        {
            return;
        }

        int index = insertIndex is null
            ? tab.EntryIds.Count
            : Math.Clamp(insertIndex.Value, 0, tab.EntryIds.Count);

        tab.EntryIds.InsertRange(index, additions);

        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveEntriesAsync(
        string tabId,
        IEnumerable<string> entryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        LauncherTab? tab = Find(tabId);

        // On Home an app is removed by hiding it, not by un-listing it.
        if (tab is null || tab.IsHome)
        {
            return;
        }

        var removals = new HashSet<string>(entryIds, StringComparer.Ordinal);

        if (tab.EntryIds.RemoveAll(removals.Contains) > 0)
        {
            await CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetOrderAsync(
        string tabId,
        IReadOnlyList<string> entryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        LauncherTab? tab = Find(tabId);
        if (tab is null)
        {
            return;
        }

        tab.EntryIds = [.. entryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];

        // A manual drag is an explicit choice of order, so stop auto-sorting this tab.
        tab.SortMode = SortMode.Manual;

        await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool Contains(string tabId, string entryId)
    {
        LauncherTab? tab = Find(tabId);

        if (tab is null || string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        // Home notionally contains everything.
        return tab.IsHome || tab.EntryIds.Contains(entryId, StringComparer.Ordinal);
    }

    public async Task<bool> PruneAsync(
        IReadOnlySet<string> knownEntryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knownEntryIds);

        int removed = 0;

        foreach (LauncherTab tab in _tabs)
        {
            removed += tab.EntryIds.RemoveAll(id => !knownEntryIds.Contains(id));
        }

        if (removed == 0)
        {
            return false;
        }

        _logger.LogInformation("Pruned {Count} stale entry references from tabs.", removed);
        await CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private LauncherTab? Find(string tabId) =>
        string.IsNullOrWhiteSpace(tabId)
            ? null
            : _tabs.FirstOrDefault(t => string.Equals(t.Id, tabId, StringComparison.Ordinal));

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        TabsChanged?.Invoke(this, EventArgs.Empty);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
