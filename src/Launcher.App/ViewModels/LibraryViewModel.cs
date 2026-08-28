using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Discovery;
using Launcher.Core.Icons;
using Launcher.Core.Launching;
using Launcher.Core.Models;
using Launcher.Core.Search;
using Launcher.Core.Services;
using Launcher.Core.Tabs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.App.ViewModels;

/// <summary>
/// Owns the tab strip and everything inside it: which apps each tab shows, every tile
/// action, and the drag-and-drop rules between tabs.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IAppTileHost
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    /// <summary>Idle delay before user edits and launch counts reach apps.json.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly IAppDiscoveryService _discovery;
    private readonly ITabService _tabs;
    private readonly IIconService _icons;
    private readonly ILaunchService _launch;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ISearchService _search;
    private readonly IWindowService _windows;
    private readonly IAppWatcherService _watcher;
    private readonly UserEntryFactory _userEntries;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _saveTimer;

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Starting up...";

    [ObservableProperty]
    private bool _isMessageOpen;

    [ObservableProperty]
    private string _messageTitle = string.Empty;

    [ObservableProperty]
    private string _messageBody = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Error;

    public LibraryViewModel(
        IAppDiscoveryService discovery,
        ITabService tabs,
        IIconService icons,
        ILaunchService launch,
        ISettingsService settings,
        IDialogService dialogs,
        UserEntryFactory userEntries,
        ISearchService search,
        IWindowService windows,
        IAppWatcherService watcher)
    {
        _discovery = discovery;
        _tabs = tabs;
        _icons = icons;
        _launch = launch;
        _settings = settings;
        _dialogs = dialogs;
        _userEntries = userEntries;
        _search = search;
        _windows = windows;
        _watcher = watcher;

        _searchCurrentTabOnly = settings.Current.SearchCurrentTabOnly;

        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = SaveDelay;
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _ = _discovery.SaveAsync();

        _discovery.EntriesChanged += (_, _) => _dispatcher.TryEnqueue(RebuildAll);
        _settings.Changed += (_, _) => _dispatcher.TryEnqueue(RebuildAll);
        _tabs.TabsChanged += (_, _) => _dispatcher.TryEnqueue(SyncTabs);

        // Fires on a background thread once a burst of installs or removals settles.
        _watcher.ChangeDetected += (_, _) => _dispatcher.TryEnqueue(() => _ = RescanAsync());
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public bool CanRescan => !IsScanning;

    /// <summary>
    /// Visibility rather than bool: bound from MainWindow, whose XAML root is a Window,
    /// and compiled-binding converters need a FrameworkElement lookup root.
    /// </summary>
    public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True while the selected tab has nothing to show.</summary>
    public bool ShowEmptyState => !IsScanning && (SelectedTab?.Items.Count ?? 0) == 0;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRescan));
        OnPropertyChanged(nameof(ScanningVisibility));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(SearchScopeLabel));
        NotifyViewSettingsChanged();

        if (value is not null)
        {
            _ = _settings.UpdateAsync(s => s.LastActiveTabId = value.Id);
        }
    }

    /// <summary>
    /// Loads tabs and the cached catalog, then scans only when there is nothing cached.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _tabs.LoadAsync();

        if (await _discovery.LoadCachedAsync())
        {
            await ReconcileTabsAsync();
        }
        else
        {
            await RescanAsync();
        }

        // Started only after the first load: an installer running during startup would
        // otherwise queue a rescan on top of the one already in flight.
        _watcher.StartWatching();
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
            await ReconcileTabsAsync();
        }
        catch (Exception ex)
        {
            ShowMessage("Scan failed", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Brings the tabs back in line with the catalog after a scan: an uninstalled app must
    /// not leave a hole in the tabs that referenced it, and a newly found game belongs in
    /// the Games tab.
    /// </summary>
    private async Task ReconcileTabsAsync()
    {
        var known = _discovery.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        bool changed = await _tabs.PruneAsync(known);

        List<string> games = [.. _discovery.Entries.Where(e => e.IsGame).Select(e => e.Id)];

        changed |= await _tabs.SyncGamesTabAsync(games);

        if (changed)
        {
            RebuildAll();
        }
    }

    // ---- tab list ----

    /// <summary>
    /// True while <see cref="SyncTabs"/> is rewriting <see cref="Tabs"/>. The tab strip
    /// reports every insert and move, and without this guard our own sync would look like
    /// a user reorder and be written straight back.
    /// </summary>
    public bool IsSyncingTabs { get; private set; }

    /// <summary>Persists a tab strip reorder performed by dragging a tab header.</summary>
    public Task ReorderTabsAsync(IReadOnlyList<string> orderedTabIds) =>
        _tabs.ReorderAsync(orderedTabIds);

    private void SyncTabs()
    {
        IsSyncingTabs = true;
        try
        {
            SyncTabsCore();
        }
        finally
        {
            IsSyncingTabs = false;
        }
    }

    /// <summary>
    /// Reconciles the view models with the service's tab list, reusing existing view
    /// models so selection and scroll position survive.
    /// </summary>
    private void SyncTabsCore()
    {
        IReadOnlyList<LauncherTab> desired = _tabs.Tabs;

        for (int i = Tabs.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(d => string.Equals(d.Id, Tabs[i].Id, StringComparison.Ordinal)))
            {
                Tabs.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            LauncherTab model = desired[i];
            TabViewModel? existing = Tabs.FirstOrDefault(
                t => string.Equals(t.Id, model.Id, StringComparison.Ordinal));

            // After a reload the service hands back fresh model instances, so a view model
            // wrapping the old one would silently write to a detached object.
            if (existing is not null && !ReferenceEquals(existing.Model, model))
            {
                Tabs.Remove(existing);
                existing = null;
            }

            if (existing is null)
            {
                Tabs.Insert(Math.Min(i, Tabs.Count), new TabViewModel(model));
                continue;
            }

            int current = Tabs.IndexOf(existing);
            if (current != i)
            {
                Tabs.Move(current, i);
            }

            existing.Name = model.Name;
            existing.Glyph = model.Glyph;
            existing.AccentColorHex = model.AccentColorHex;
            existing.ViewMode = model.ViewMode;
            existing.TileScale = model.TileScale;
        }

        RebuildAll();
        RestoreSelection();
    }

    private void RestoreSelection()
    {
        if (Tabs.Count == 0)
        {
            SelectedTab = null;
            return;
        }

        if (SelectedTab is not null && Tabs.Contains(SelectedTab))
        {
            return;
        }

        string? wanted = _settings.Current.LastActiveTabId;

        SelectedTab = Tabs.FirstOrDefault(t => string.Equals(t.Id, wanted, StringComparison.Ordinal))
            ?? Tabs[0];
    }

    [RelayCommand]
    private async Task AddTabAsync()
    {
        TabEdit? edit = await _dialogs.EditTabAsync("New tab", "New tab", null, null);
        if (edit is null)
        {
            return;
        }

        LauncherTab created = await _tabs.CreateTabAsync(edit.Name, edit.Glyph, edit.AccentColorHex);

        // A new tab starts from the defaults on the Settings page rather than the model's
        // own hard-coded values.
        AppSettings settings = _settings.Current;
        await _tabs.SetViewAsync(created.Id, settings.DefaultViewMode, settings.DefaultTileScale);

        SelectedTab = Tabs.FirstOrDefault(t => string.Equals(t.Id, created.Id, StringComparison.Ordinal));
    }

    /// <summary>Renames and restyles a tab. Home can be restyled but not renamed.</summary>
    public async Task EditTabAsync(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        TabEdit? edit = await _dialogs.EditTabAsync(
            tab.IsHome ? "Home tab" : "Edit tab",
            tab.Name,
            tab.Glyph,
            tab.AccentColorHex);

        if (edit is null)
        {
            return;
        }

        if (!tab.IsHome)
        {
            await _tabs.RenameTabAsync(tab.Id, edit.Name);
        }

        await _tabs.SetAppearanceAsync(tab.Id, edit.Glyph, edit.AccentColorHex);
    }

    /// <summary>Deletes a tab after confirming. The apps in it are never touched.</summary>
    public async Task<bool> RequestDeleteTabAsync(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!tab.CanClose)
        {
            return false;
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            "Delete " + tab.Name + "?",
            "The tab and its arrangement are removed. None of the apps in it are uninstalled or removed from Home.",
            "Delete tab");

        if (!confirmed)
        {
            return false;
        }

        return await _tabs.DeleteTabAsync(tab.Id);
    }

    /// <summary>Persists a tab strip reorder performed by dragging a tab header.</summary>
    public Task MoveTabAsync(string tabId, int targetIndex) => _tabs.MoveTabAsync(tabId, targetIndex);

    // ---- tab contents ----

    private void RebuildAll()
    {
        foreach (TabViewModel tab in Tabs)
        {
            Rebuild(tab);
        }

        UpdateStatus();
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void Rebuild(TabViewModel tab)
    {
        AppSettings settings = _settings.Current;

        var byId = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
        foreach (AppEntry entry in _discovery.Entries)
        {
            byId[entry.Id] = entry;
        }

        bool IsVisible(AppEntry e) =>
            (settings.ShowHiddenEntries || !e.IsHidden)
            && (settings.ShowFilteredEntries || !e.IsFiltered);

        // Home shows everything; a custom tab shows exactly what was put in it.
        IEnumerable<AppEntry> members = tab.IsHome
            ? _discovery.Entries.Where(IsVisible)
            : tab.Model.EntryIds
                .Select(id => byId.TryGetValue(id, out AppEntry? e) ? e : null)
                .Where(e => e is not null && IsVisible(e))
                .Select(e => e!);

        List<AppEntry> entries = OrderEntries(members, tab.Model);

        // Suppress order persistence: the clear/add churn below is not a manual reorder.
        tab.IsRebuilding = true;
        try
        {
            tab.Items.Clear();
            foreach (AppEntry entry in entries)
            {
                tab.Items.Add(new AppTileViewModel(entry, tab, _icons, _launch, this));
            }
        }
        finally
        {
            tab.IsRebuilding = false;
        }
    }

    /// <summary>
    /// Applies the tab's sort. Name is always the final tie-break so equal entries do not
    /// swap places between rebuilds.
    /// <para>
    /// Favourites deliberately do not float to the top: a sort labelled "A to Z" that is
    /// not actually A to Z is worse than no sort at all. The star badge marks them instead.
    /// </para>
    /// </summary>
    private static List<AppEntry> OrderEntries(IEnumerable<AppEntry> entries, LauncherTab tab)
    {
        return tab.SortMode switch
        {
            SortMode.Alphabetical =>
                [.. entries.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)],

            SortMode.MostUsed =>
                [.. entries
                    .OrderByDescending(e => e.LaunchCount)
                    .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)],

            SortMode.RecentlyUsed =>
                [.. entries
                    .OrderByDescending(e => e.LastLaunchedUtc ?? DateTimeOffset.MinValue)
                    .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)],

            _ => ManualOrder(entries, tab),
        };
    }

    /// <summary>
    /// Manual order, from the tab's stored id list. Anything not in that list is appended
    /// alphabetically rather than dropped - on Home the list is only an order hint, so
    /// this is what stops a newly installed app disappearing after a manual reorder.
    /// </summary>
    private static List<AppEntry> ManualOrder(IEnumerable<AppEntry> entries, LauncherTab tab)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tab.EntryIds.Count; i++)
        {
            rank[tab.EntryIds[i]] = i;
        }

        return [.. entries
            .OrderBy(e => rank.TryGetValue(e.Id, out int index) ? index : int.MaxValue)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private void UpdateStatus()
    {
        int total = _discovery.Entries.Count;
        int filtered = _discovery.Entries.Count(e => e.IsFiltered);
        int hidden = _discovery.Entries.Count(e => e.IsHidden);
        int shown = SelectedTab?.Items.Count ?? 0;

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "{0} shown  ·  {1} apps discovered, {2} filtered, {3} hidden",
            shown,
            total,
            filtered,
            hidden);
    }

    // ---- view mode, tile size and sort (all per tab) ----

    /// <summary>Index into the view-mode radio buttons for the selected tab.</summary>
    public int ViewModeIndex
    {
        get => (int)(SelectedTab?.ViewMode ?? ViewMode.MediumGrid);
        set
        {
            // RadioButtons reports -1 while its items are still loading.
            if (value >= 0 && Enum.IsDefined((ViewMode)value) && value != ViewModeIndex)
            {
                _ = ApplyViewAsync(viewMode: (ViewMode)value);
            }
        }
    }

    public int SortModeIndex
    {
        get => (int)(SelectedTab?.Model.SortMode ?? SortMode.Manual);
        set
        {
            if (value >= 0 && Enum.IsDefined((SortMode)value) && value != SortModeIndex)
            {
                _ = ApplyViewAsync(sortMode: (SortMode)value);
            }
        }
    }

    /// <summary>
    /// Tile size as a percentage, which is what the slider works in. The slider's range
    /// mirrors LauncherTab.MinTileScale..MaxTileScale; the service clamps regardless.
    /// </summary>
    public double TileScalePercent
    {
        get => (SelectedTab?.TileScale ?? 1.0) * 100;
        set
        {
            if (Math.Abs(value - TileScalePercent) > 0.5)
            {
                _ = ApplyViewAsync(tileScale: value / 100);
            }
        }
    }


    private async Task ApplyViewAsync(
        ViewMode? viewMode = null,
        double? tileScale = null,
        SortMode? sortMode = null)
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        // The service clamps and persists; SyncTabs then pushes the stored values back
        // into the view models, so the UI never diverges from what was saved.
        await _tabs.SetViewAsync(tab.Id, viewMode, tileScale, sortMode);

        NotifyViewSettingsChanged();
    }

    private void NotifyViewSettingsChanged()
    {
        OnPropertyChanged(nameof(ViewModeIndex));
        OnPropertyChanged(nameof(SortModeIndex));
        OnPropertyChanged(nameof(TileScalePercent));
    }

    // ---- search ----

    /// <summary>Results for the current query, best first. Empty when search is inactive.</summary>
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _selectedResultIndex = -1;

    [ObservableProperty]
    private bool _searchCurrentTabOnly;

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);

    /// <summary>Visibility rather than bool - see <see cref="ScanningVisibility"/>.</summary>
    public Visibility SearchVisibility => IsSearchActive ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TabContentVisibility => IsSearchActive ? Visibility.Collapsed : Visibility.Visible;

    public string SearchSummary => SearchResults.Count switch
    {
        0 => "No matches",
        1 => "1 match",
        _ => string.Format(CultureInfo.CurrentCulture, "{0} matches", SearchResults.Count),
    };

    public string SearchScopeLabel => SearchCurrentTabOnly && SelectedTab is not null
        ? "In " + SelectedTab.Name
        : "All tabs";

    partial void OnSearchQueryChanged(string value) => RunSearch();

    partial void OnSearchCurrentTabOnlyChanged(bool value)
    {
        _ = _settings.UpdateAsync(s => s.SearchCurrentTabOnly = value);
        OnPropertyChanged(nameof(SearchScopeLabel));
        NotifyViewSettingsChanged();
        RunSearch();
    }

    private void RunSearch()
    {
        IEnumerable<AppEntry> candidates = SearchCurrentTabOnly && SelectedTab is not null
            ? SelectedTab.Items.Select(tile => tile.Entry)
            : VisibleEntries();

        IReadOnlyList<SearchResult> results = _search.Search(SearchQuery, candidates);

        SearchResults.Clear();
        foreach (SearchResult result in results)
        {
            SearchResults.Add(new SearchResultViewModel(result, _icons));
        }

        // Pre-select the top hit so Enter launches it without any arrow keys.
        SelectedResultIndex = SearchResults.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(SearchVisibility));
        OnPropertyChanged(nameof(TabContentVisibility));
        OnPropertyChanged(nameof(SearchSummary));
    }

    /// <summary>Entries eligible for search, honouring the hidden and filtered toggles.</summary>
    private IEnumerable<AppEntry> VisibleEntries()
    {
        AppSettings settings = _settings.Current;

        return _discovery.Entries.Where(e =>
            (settings.ShowHiddenEntries || !e.IsHidden)
            && (settings.ShowFilteredEntries || !e.IsFiltered));
    }

    /// <summary>Moves the highlighted result, clamped rather than wrapping.</summary>
    public void MoveResultSelection(int delta)
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        SelectedResultIndex = Math.Clamp(SelectedResultIndex + delta, 0, SearchResults.Count - 1);
    }

    /// <summary>Launches the highlighted result and dismisses the search.</summary>
    public async Task LaunchSelectedResultAsync()
    {
        if (SelectedResultIndex < 0 || SelectedResultIndex >= SearchResults.Count)
        {
            return;
        }

        AppEntry entry = SearchResults[SelectedResultIndex].Entry;

        ClearSearch();
        await LaunchEntryAsync(entry, asAdministrator: false);
    }

    public async Task LaunchResultAsync(SearchResultViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);

        AppEntry entry = result.Entry;

        ClearSearch();
        await LaunchEntryAsync(entry, asAdministrator: false);
    }

    [RelayCommand]
    public void ClearSearch() => SearchQuery = string.Empty;

    // ---- drag and drop ----

    /// <summary>
    /// Applies the drop rules from SPEC.md: from Home it copies, because Home keeps
    /// everything; out of a custom tab it moves, removing it from the source only.
    /// </summary>
    public async Task DropEntriesOnTabAsync(
        IReadOnlyList<string> entryIds,
        string? sourceTabId,
        string targetTabId,
        int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        if (entryIds.Count == 0 || string.Equals(sourceTabId, targetTabId, StringComparison.Ordinal))
        {
            return;
        }

        await _tabs.AddEntriesAsync(targetTabId, entryIds, insertIndex);

        bool cameFromACustomTab = sourceTabId is not null
            && !string.Equals(sourceTabId, LauncherTab.HomeId, StringComparison.Ordinal);

        if (cameFromACustomTab)
        {
            await _tabs.RemoveEntriesAsync(sourceTabId!, entryIds);
        }
    }

    /// <summary>Persists a manual reorder inside one tab.</summary>
    public async Task PersistOrderAsync(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (tab.IsRebuilding)
        {
            return;
        }

        await _tabs.SetOrderAsync(tab.Id, [.. tab.Items.Select(i => i.Entry.Id)]);
    }

    /// <summary>Adds files, folders and shortcuts dropped in from Explorer.</summary>
    public async Task AddDroppedPathsAsync(IReadOnlyList<string> paths, TabViewModel targetTab)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetTab);

        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<AppEntry> created = await _userEntries.CreateAsync(
                paths,
                AppDiscoveryService.IconPixelSize);

            if (created.Count == 0)
            {
                ShowMessage(
                    "Nothing was added",
                    "None of those items could be turned into an app entry.",
                    InfoBarSeverity.Warning);
                return;
            }

            // Merging by id means dropping a shortcut for an already-discovered app
            // reuses that entry instead of creating a duplicate tile.
            IReadOnlyList<string> ids = await _discovery.AddOrMergeAsync(created);

            await _tabs.AddEntriesAsync(targetTab.Id, ids);

            ShowMessage(
                "Added " + ids.Count + (ids.Count == 1 ? " item" : " items"),
                "Dropped items were added to " + targetTab.Name + ".",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage("Could not add the dropped items", ex.Message, InfoBarSeverity.Error);
        }
    }

    // ---- IAppTileHost ----

    public Task LaunchAsync(AppTileViewModel tile, bool asAdministrator)
    {
        ArgumentNullException.ThrowIfNull(tile);
        return LaunchEntryAsync(tile.Entry, asAdministrator);
    }

    /// <summary>Shared by tiles and search results.</summary>
    public async Task LaunchEntryAsync(AppEntry entry, bool asAdministrator)
    {
        ArgumentNullException.ThrowIfNull(entry);

        LaunchResult result = await _launch.LaunchAsync(entry, asAdministrator);

        if (result.Succeeded)
        {
            QueueSave();

            if (_settings.Current.HideAfterLaunch)
            {
                _windows.Hide();
            }

            return;
        }

        // A declined UAC prompt is a decision, not a failure.
        if (result.WasCancelled)
        {
            return;
        }

        ShowMessage(
            "Could not start " + entry.DisplayName,
            result.ErrorMessage ?? "Windows did not report a reason.",
            InfoBarSeverity.Error);
    }

    public async Task OpenFileLocationAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        LaunchResult result = await _launch.OpenFileLocationAsync(tile.Entry);

        if (!result.Succeeded && !result.WasCancelled)
        {
            ShowMessage(
                "Could not open the file location",
                result.ErrorMessage ?? "Windows did not report a reason.",
                InfoBarSeverity.Error);
        }
    }

    public async Task RenameAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        string? name = await _dialogs.PromptForTextAsync(
            "Rename",
            "This changes the name shown here. The original name is kept and used again if you clear this.",
            tile.DisplayName,
            "Rename");

        if (name is null)
        {
            return;
        }

        tile.Entry.DisplayName = string.IsNullOrWhiteSpace(name) ? tile.Entry.OriginalName : name;
        tile.DisplayName = tile.Entry.DisplayName;

        await _discovery.SaveAsync();
        RebuildAll();
    }

    public async Task ChangeIconAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        string? picked = await _dialogs.PickIconSourceAsync();
        if (picked is null)
        {
            return;
        }

        if (ImageExtensions.Contains(Path.GetExtension(picked)))
        {
            // Use the image as-is; running it through the shell would letterbox it.
            tile.Entry.CustomIconPath = picked;
        }
        else
        {
            string? cacheFile = await _icons.ExtractFromPathAsync(picked, AppDiscoveryService.IconPixelSize);
            string? cached = _icons.ResolveCachedPath(cacheFile);

            if (cached is null)
            {
                ShowMessage(
                    "No icon found",
                    "That file does not contain an icon this launcher can read.",
                    InfoBarSeverity.Warning);
                return;
            }

            tile.Entry.CustomIconPath = cached;
        }

        tile.ReloadIcon();
        await _discovery.SaveAsync();
    }

    public async Task ResetIconAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        tile.Entry.CustomIconPath = null;
        tile.ReloadIcon();

        await _discovery.SaveAsync();
    }

    public async Task EditLaunchOptionsAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        LaunchOptionsEdit? edit = await _dialogs.EditLaunchOptionsAsync(tile.Entry);
        if (edit is null)
        {
            return;
        }

        tile.Entry.Arguments = edit.Arguments;
        tile.Entry.WorkingDirectory = edit.WorkingDirectory;

        await _discovery.SaveAsync();
    }

    public async Task ToggleFavoriteAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        tile.Entry.IsFavorite = !tile.Entry.IsFavorite;
        tile.IsFavorite = tile.Entry.IsFavorite;

        await _discovery.SaveAsync();
        RebuildAll();
    }

    public async Task ToggleHiddenAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        bool nowHidden = !tile.Entry.IsHidden;
        tile.Entry.IsHidden = nowHidden;
        tile.IsHidden = nowHidden;

        await _discovery.SaveAsync();

        if (nowHidden && !_settings.Current.ShowHiddenEntries)
        {
            ShowMessage(
                tile.DisplayName + " is hidden",
                "Turn on \"Show hidden entries\" in Settings to bring it back.",
                InfoBarSeverity.Informational);
        }

        RebuildAll();
    }

    public Task ShowPropertiesAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        string? iconPath = tile.Entry.CustomIconPath is { Length: > 0 } custom
            ? custom
            : _icons.ResolveCachedPath(tile.Entry.IconCacheFile);

        return _dialogs.ShowPropertiesAsync(tile.Entry, iconPath);
    }

    public Task PinToTabAsync(AppTileViewModel tile, string tabId)
    {
        ArgumentNullException.ThrowIfNull(tile);
        return _tabs.AddEntriesAsync(tabId, [tile.Entry.Id]);
    }

    public Task UnpinAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        return tile.CanUnpin
            ? _tabs.RemoveEntriesAsync(tile.Owner.Id, [tile.Entry.Id])
            : Task.CompletedTask;
    }

    [RelayCommand]
    private void DismissMessage() => IsMessageOpen = false;

    private void ShowMessage(string title, string body, InfoBarSeverity severity)
    {
        MessageTitle = title;
        MessageBody = body;
        MessageSeverity = severity;
        IsMessageOpen = true;
    }

    /// <summary>Coalesces rapid writes so a burst of launches is one file rewrite.</summary>
    private void QueueSave() => _saveTimer.Start();
}
