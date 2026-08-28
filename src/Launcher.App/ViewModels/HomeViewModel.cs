using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Discovery;
using Launcher.Core.Icons;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.UI.Dispatching;

namespace Launcher.App.ViewModels;

/// <summary>
/// Drives the Phase 2 discovery list: show the cached catalog immediately, then scan in
/// the background while the window stays usable.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IAppDiscoveryService _discovery;
    private readonly IIconService _icons;
    private readonly ISettingsService _settings;
    private readonly DispatcherQueue _dispatcher;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Starting up...";

    [ObservableProperty]
    private bool _hasNoItems = true;

    public HomeViewModel(IAppDiscoveryService discovery, IIconService icons, ISettingsService settings)
    {
        _discovery = discovery;
        _icons = icons;
        _settings = settings;

        // Captured at construction, on the UI thread, so work finishing on the scan's STA
        // and thread pool threads can be marshalled back.
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // The list is rebuilt from these two events rather than inline after each await.
        // That way a rescan started from the Settings page refreshes Home too, and there
        // is exactly one code path that decides what is on screen.
        _discovery.EntriesChanged += (_, _) => _dispatcher.TryEnqueue(Rebuild);
        _settings.Changed += (_, _) => _dispatcher.TryEnqueue(Rebuild);
    }

    public ObservableCollection<AppListItem> Items { get; } = [];

    /// <summary>The empty state must not flash while the first scan is still running.</summary>
    public bool ShowEmptyState => HasNoItems && !IsScanning;

    public bool CanRescan => !IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanRescan));
    }

    partial void OnHasNoItemsChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    /// <summary>
    /// Shows whatever was cached, then scans only when there is nothing to show. A first
    /// run scans; later runs start instantly from apps.json.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (await _discovery.LoadCachedAsync())
        {
            return;
        }

        await RescanAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Looking for installed apps...";

        var progress = new Progress<DiscoveryProgress>(report => _dispatcher.TryEnqueue(() =>
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                "Scanning {0}: {1} of {2}...",
                report.SourceName,
                report.Completed,
                report.Total)));

        try
        {
            await _discovery.ScanAsync(progress);
        }
        catch (Exception ex)
        {
            // Phase 3 surfaces failures in an InfoBar; for now the status line carries it.
            StatusText = "Scan failed: " + ex.Message;
            return;
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Rebuilds the visible list from the catalog and the current settings, and restates
    /// the status line. Always runs on the UI thread.
    /// </summary>
    private void Rebuild()
    {
        bool showFiltered = _settings.Current.ShowFilteredEntries;

        List<AppEntry> visible = [.. _discovery.Entries
            .Where(e => !e.IsHidden && (showFiltered || !e.IsFiltered))
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

        Items.Clear();
        foreach (AppEntry entry in visible)
        {
            Items.Add(new AppListItem(entry, _icons.ResolveCachedPath(entry.IconCacheFile)));
        }

        HasNoItems = Items.Count == 0;

        int total = _discovery.Entries.Count;
        int filtered = _discovery.Entries.Count(e => e.IsFiltered);

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "Showing {0} of {1} discovered  ·  {2} filtered out",
            Items.Count,
            total,
            filtered);
    }
}
