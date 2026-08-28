using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Discovery;
using Launcher.Core.Icons;
using Launcher.Core.Launching;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.App.ViewModels;

/// <summary>
/// Drives the Home grid: shows the cached catalog immediately, scans in the background,
/// and performs every tile action.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IAppTileHost
{
    /// <summary>Image extensions used directly instead of being run through the shell.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    /// <summary>Idle delay before user edits and launch counts are written to apps.json.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly IAppDiscoveryService _discovery;
    private readonly IIconService _icons;
    private readonly ILaunchService _launch;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _saveTimer;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Starting up...";

    [ObservableProperty]
    private bool _hasNoItems = true;

    [ObservableProperty]
    private bool _isMessageOpen;

    [ObservableProperty]
    private string _messageTitle = string.Empty;

    [ObservableProperty]
    private string _messageBody = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Error;

    public HomeViewModel(
        IAppDiscoveryService discovery,
        IIconService icons,
        ILaunchService launch,
        ISettingsService settings,
        IDialogService dialogs)
    {
        _discovery = discovery;
        _icons = icons;
        _launch = launch;
        _settings = settings;
        _dialogs = dialogs;

        // Captured on the UI thread so work finishing on the scan's STA and thread pool
        // threads can be marshalled back.
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = SaveDelay;
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _ = _discovery.SaveAsync();

        // One code path decides what is on screen, so a rescan started from Settings
        // refreshes Home too.
        _discovery.EntriesChanged += (_, _) => _dispatcher.TryEnqueue(Rebuild);
        _settings.Changed += (_, _) => _dispatcher.TryEnqueue(Rebuild);
    }

    public ObservableCollection<AppTileViewModel> Items { get; } = [];

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
            ShowMessage("Scan failed", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Rebuilds the grid from the catalog and the current settings.</summary>
    private void Rebuild()
    {
        AppSettings settings = _settings.Current;

        List<AppEntry> visible = [.. _discovery.Entries
            .Where(e => (settings.ShowHiddenEntries || !e.IsHidden)
                     && (settings.ShowFilteredEntries || !e.IsFiltered))
            .OrderByDescending(e => e.IsFavorite)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

        Items.Clear();
        foreach (AppEntry entry in visible)
        {
            Items.Add(new AppTileViewModel(entry, _icons, _launch, this));
        }

        HasNoItems = Items.Count == 0;

        int total = _discovery.Entries.Count;
        int filtered = _discovery.Entries.Count(e => e.IsFiltered);
        int hidden = _discovery.Entries.Count(e => e.IsHidden);

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "{0} of {1} apps  ·  {2} filtered, {3} hidden",
            Items.Count,
            total,
            filtered,
            hidden);
    }

    // ---- IAppTileHost ----

    public async Task LaunchAsync(AppTileViewModel tile, bool asAdministrator)
    {
        ArgumentNullException.ThrowIfNull(tile);

        LaunchResult result = await _launch.LaunchAsync(tile.Entry, asAdministrator);

        if (result.Succeeded)
        {
            // Launch counts feed the search ranking in Phase 5, so they are worth saving.
            QueueSave();
            return;
        }

        // A declined UAC prompt is a decision, not a failure. Saying nothing is correct.
        if (result.WasCancelled)
        {
            return;
        }

        ShowMessage(
            "Could not start " + tile.DisplayName,
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

        // Clearing the field restores the discovered name rather than leaving a blank tile.
        tile.Entry.DisplayName = string.IsNullOrWhiteSpace(name) ? tile.Entry.OriginalName : name;
        tile.DisplayName = tile.Entry.DisplayName;

        await _discovery.SaveAsync();

        // The grid is sorted by name, so a rename changes where the tile belongs.
        Rebuild();
    }

    public async Task ChangeIconAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        string? picked = await _dialogs.PickIconSourceAsync();
        if (picked is null)
        {
            return;
        }

        string extension = Path.GetExtension(picked);

        if (ImageExtensions.Contains(extension))
        {
            // Use the image as-is; running it through the shell would letterbox it.
            tile.Entry.CustomIconPath = picked;
        }
        else
        {
            // An .exe, .dll, .ico or .lnk: pull its icon out through the shell.
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

        // Favorites sort to the front.
        Rebuild();
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

        Rebuild();
    }

    public Task ShowPropertiesAsync(AppTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        string? iconPath = tile.Entry.CustomIconPath is { Length: > 0 } custom
            ? custom
            : _icons.ResolveCachedPath(tile.Entry.IconCacheFile);

        return _dialogs.ShowPropertiesAsync(tile.Entry, iconPath);
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

    /// <summary>
    /// Coalesces rapid writes - launching several apps in a row should not mean several
    /// full rewrites of apps.json.
    /// </summary>
    private void QueueSave() => _saveTimer.Start();
}
