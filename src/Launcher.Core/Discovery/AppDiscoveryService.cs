using Launcher.Core.Models;
using Launcher.Core.Services;
using Launcher.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Discovery;

/// <inheritdoc cref="IAppDiscoveryService"/>
public sealed class AppDiscoveryService : IAppDiscoveryService, IDisposable
{
    /// <summary>
    /// Icons are extracted once at a size large enough for the biggest tile, then scaled
    /// down for smaller views. Re-extracting per view mode would defeat the cache.
    /// </summary>
    public const int IconPixelSize = 96;

    private readonly IReadOnlyList<IAppSource> _sources;
    private readonly IStorageService _storage;
    private readonly StoragePaths _paths;
    private readonly ISettingsService _settings;
    private readonly JunkFilter _filter;
    private readonly ILogger<AppDiscoveryService> _logger;

    /// <summary>Serializes scans so a rescan cannot interleave with one already running.</summary>
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    private List<AppEntry> _entries = [];

    public AppDiscoveryService(
        IEnumerable<IAppSource> sources,
        IStorageService storage,
        StoragePaths paths,
        ISettingsService settings,
        JunkFilter? filter = null,
        ILogger<AppDiscoveryService>? logger = null)
    {
        _sources = sources?.ToArray() ?? [];
        _storage = storage;
        _paths = paths;
        _settings = settings;
        _filter = filter ?? new JunkFilter();
        _logger = logger ?? NullLogger<AppDiscoveryService>.Instance;
    }

    public IReadOnlyList<AppEntry> Entries => _entries;

    public bool IsScanning { get; private set; }

    public event EventHandler? EntriesChanged;

    public async Task<bool> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        AppCatalog? catalog = await _storage
            .LoadAsync<AppCatalog>(_paths.AppsFile, cancellationToken)
            .ConfigureAwait(false);

        if (catalog is null || catalog.Entries.Count == 0)
        {
            return false;
        }

        _entries = catalog.Entries;
        EntriesChanged?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation("Loaded {Count} cached entries.", _entries.Count);
        return true;
    }

    public async Task ScanAsync(
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        IsScanning = true;

        try
        {
            var context = new DiscoveryContext(IconPixelSize, progress);
            List<AppEntry> discovered = await RunSourcesAsync(context, cancellationToken).ConfigureAwait(false);

            foreach (AppEntry entry in discovered)
            {
                FilterReason reason = _filter.Evaluate(entry);
                entry.IsFiltered = reason != FilterReason.None;
                entry.FilterReason = reason;
            }

            List<AppEntry> merged = AppDeduplicator.Merge(discovered);
            _entries = ReconcileWithExisting(merged);

            await SaveCatalogAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Scan complete: {Total} entries, {Visible} visible.",
                _entries.Count,
                _entries.Count(e => e.IsVisibleOnHome));

            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsScanning = false;
            _scanLock.Release();
        }
    }

    /// <summary>
    /// Runs the enabled sources concurrently. The Start Menu walk occupies its own STA
    /// thread while the package catalog work sits on the thread pool, so they overlap
    /// rather than queue.
    /// </summary>
    private async Task<List<AppEntry>> RunSourcesAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        AppSettings settings = _settings.Current;

        List<IAppSource> enabled = [.. _sources.Where(s => IsEnabled(s, settings))];

        IReadOnlyList<AppEntry>[] results = await Task.WhenAll(
            enabled.Select(source => RunSourceSafelyAsync(source, context, cancellationToken)))
            .ConfigureAwait(false);

        var all = new List<AppEntry>();
        foreach (IReadOnlyList<AppEntry> result in results)
        {
            all.AddRange(result);
        }

        return all;
    }

    private async Task<IReadOnlyList<AppEntry>> RunSourceSafelyAsync(
        IAppSource source,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.DiscoverAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One broken source must not cost us every other source's results.
            _logger.LogError(ex, "Source {Source} failed; continuing without it.", source.DisplayName);
            return [];
        }
    }

    private static bool IsEnabled(IAppSource source, AppSettings settings) => source.Kind switch
    {
        AppSource.StartMenu => settings.ScanStartMenu,
        AppSource.Packaged => settings.ScanPackagedApps,
        _ => false,
    };

    /// <summary>
    /// Carries user edits across a rescan. Entries are matched by id, which is derived
    /// from the merge key, so a rename or a custom icon survives.
    /// </summary>
    private List<AppEntry> ReconcileWithExisting(List<AppEntry> scanned)
    {
        var existingById = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
        foreach (AppEntry entry in _entries)
        {
            existingById[entry.Id] = entry;
        }

        var result = new List<AppEntry>(scanned.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (AppEntry entry in scanned)
        {
            if (existingById.TryGetValue(entry.Id, out AppEntry? existing))
            {
                existing.UpdateFromScan(entry);
                result.Add(existing);
            }
            else
            {
                result.Add(entry);
            }

            seen.Add(entry.Id);
        }

        // Entries the user created by dragging something in are not produced by any
        // source, so they would otherwise be dropped on every rescan.
        foreach (AppEntry entry in _entries)
        {
            if (entry.Source == AppSource.UserAdded && seen.Add(entry.Id))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        SaveCatalogAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> AddOrMergeAsync(
        IEnumerable<AppEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var existingById = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
        foreach (AppEntry entry in _entries)
        {
            existingById[entry.Id] = entry;
        }

        var ids = new List<string>();
        bool added = false;

        foreach (AppEntry candidate in entries)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
            {
                continue;
            }

            if (existingById.TryGetValue(candidate.Id, out AppEntry? existing))
            {
                // Already known - reuse it rather than duplicating the tile.
                ids.Add(existing.Id);
                continue;
            }

            _entries.Add(candidate);
            existingById[candidate.Id] = candidate;
            ids.Add(candidate.Id);
            added = true;
        }

        if (added)
        {
            await SaveCatalogAsync(cancellationToken).ConfigureAwait(false);
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        return ids;
    }

    private async Task SaveCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = new AppCatalog
        {
            LastScanUtc = DateTimeOffset.UtcNow,
            Entries = _entries,
        };

        try
        {
            await _storage.SaveAsync(_paths.AppsFile, catalog, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The in-memory catalog is still good; the next scan will try again.
            _logger.LogError(ex, "Could not write {Path}.", _paths.AppsFile);
        }
    }

    public void Dispose() => _scanLock.Dispose();
}
