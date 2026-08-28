using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Discovery;
using Launcher.Core.Icons;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Launcher.Core.Storage;
using Launcher.Core.Tabs;

namespace Launcher.App.ViewModels;

/// <summary>Backs the Settings page.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly StoragePaths _paths;
    private readonly IAppDiscoveryService _discovery;
    private readonly IIconService _icons;
    private readonly IStartupService _startup;
    private readonly IHotkeyService _hotkeys;
    private readonly IDialogService _dialogs;
    private readonly IConfigArchiveService _archive;
    private readonly ITabService _tabs;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedBackdropIndex;

    [ObservableProperty]
    private bool _scanStartMenu;

    [ObservableProperty]
    private bool _scanPackagedApps;

    [ObservableProperty]
    private bool _showFilteredEntries;

    [ObservableProperty]
    private bool _showHiddenEntries;

    [ObservableProperty]
    private int _defaultViewModeIndex;

    [ObservableProperty]
    private double _defaultTileScalePercent = 100;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _hideAfterLaunch;

    [ObservableProperty]
    private bool _hotkeyEnabled;

    [ObservableProperty]
    private string _hotkeyText = string.Empty;

    [ObservableProperty]
    private string _hotkeyStatus = string.Empty;

    [ObservableProperty]
    private string _startupStatus = string.Empty;

    [ObservableProperty]
    private string _iconCacheStatus = string.Empty;

    [ObservableProperty]
    private string _archiveStatus = "Save your tabs, settings and icons to a zip, or restore them from one.";

    /// <summary>Suppresses write-back while the view model seeds itself from stored settings.</summary>
    private bool _isInitializing;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        StoragePaths paths,
        IAppDiscoveryService discovery,
        IIconService icons,
        IStartupService startup,
        IHotkeyService hotkeys,
        IDialogService dialogs,
        IConfigArchiveService archive,
        ITabService tabs)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _discovery = discovery;
        _icons = icons;
        _startup = startup;
        _hotkeys = hotkeys;
        _dialogs = dialogs;
        _archive = archive;
        _tabs = tabs;

        VersionDescription = BuildVersionDescription();

        ReloadFromSettings();
    }

    /// <summary>Folder holding settings.json, apps.json, tabs.json and the icon cache.</summary>
    public string StorageLocation => _paths.Root;

    public string StorageModeDescription => _paths.IsPortable
        ? "Portable mode: state is stored beside the executable because portable.txt is present."
        : "State is stored in your local application data folder.";

    public string VersionDescription { get; }

    public string AccentDescription { get; } =
        "The launcher follows the accent colour you have chosen for Windows.";

    /// <summary>Seeds every bound property from the stored settings without writing back.</summary>
    private void ReloadFromSettings()
    {
        AppSettings current = _settings.Current;

        _isInitializing = true;
        try
        {
            SelectedThemeIndex = (int)current.Theme;
            SelectedBackdropIndex = (int)current.Backdrop;
            ScanStartMenu = current.ScanStartMenu;
            ScanPackagedApps = current.ScanPackagedApps;
            ShowFilteredEntries = current.ShowFilteredEntries;
            ShowHiddenEntries = current.ShowHiddenEntries;
            DefaultViewModeIndex = (int)current.DefaultViewMode;
            DefaultTileScalePercent = current.DefaultTileScale * 100;
            MinimizeToTray = current.MinimizeToTray;
            HideAfterLaunch = current.HideAfterLaunch;
            StartMinimized = current.StartMinimized;
            HotkeyEnabled = current.HotkeyEnabled;
            HotkeyText = current.Hotkey.ToString();

            // Read from the registry rather than from settings.json: the user may have
            // removed the Run entry outside the app, and the toggle should reflect reality.
            StartWithWindows = _startup.IsEnabled();
        }
        finally
        {
            _isInitializing = false;
        }

        UpdateHotkeyStatus();
        UpdateStartupStatus();
    }

    private static string BuildVersionDescription()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;

        return version is null
            ? "Unknown version"
            : string.Format(
                CultureInfo.InvariantCulture,
                "Version {0}.{1}.{2}",
                version.Major,
                version.Minor,
                version.Build);
    }

    // ---- appearance ----

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing || !Enum.IsDefined(typeof(AppTheme), value))
        {
            return;
        }

        var theme = (AppTheme)value;
        _theme.ApplyTheme(theme);
        _ = _settings.UpdateAsync(s => s.Theme = theme);
    }

    partial void OnSelectedBackdropIndexChanged(int value)
    {
        if (_isInitializing || !Enum.IsDefined(typeof(BackdropKind), value))
        {
            return;
        }

        var backdrop = (BackdropKind)value;
        _theme.ApplyBackdrop(backdrop);
        _ = _settings.UpdateAsync(s => s.Backdrop = backdrop);
    }

    partial void OnDefaultViewModeIndexChanged(int value)
    {
        // Applies to tabs created from here on; existing tabs keep their own choice.
        if (!_isInitializing && value >= 0 && Enum.IsDefined((ViewMode)value))
        {
            _ = _settings.UpdateAsync(s => s.DefaultViewMode = (ViewMode)value);
        }
    }

    partial void OnDefaultTileScalePercentChanged(double value)
    {
        if (!_isInitializing)
        {
            double scale = Math.Clamp(value / 100, LauncherTab.MinTileScale, LauncherTab.MaxTileScale);
            _ = _settings.UpdateAsync(s => s.DefaultTileScale = scale);
        }
    }

    /// <summary>Opens the Windows colour settings, where the accent actually lives.</summary>
    [RelayCommand]
    private static void OpenWindowsColorSettings() => TryOpen("ms-settings:colors");

    // ---- discovery ----

    partial void OnScanStartMenuChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.ScanStartMenu = value);
        }
    }

    partial void OnScanPackagedAppsChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.ScanPackagedApps = value);
        }
    }

    partial void OnShowFilteredEntriesChanged(bool value)
    {
        // Filtered entries stay in the catalog, so revealing them needs no rescan -
        // the settings-changed event alone rebuilds the list.
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.ShowFilteredEntries = value);
        }
    }

    partial void OnShowHiddenEntriesChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.ShowHiddenEntries = value);
        }
    }

    /// <summary>Lets the user pick hidden apps to bring back.</summary>
    [RelayCommand]
    private async Task ManageHiddenAppsAsync()
    {
        var hidden = _discovery.Entries.Where(e => e.IsHidden).ToList();

        IReadOnlyList<string> unhide = await _dialogs.ManageHiddenAppsAsync(hidden);
        if (unhide.Count == 0)
        {
            return;
        }

        var ids = new HashSet<string>(unhide, StringComparer.Ordinal);

        foreach (AppEntry entry in _discovery.Entries.Where(e => ids.Contains(e.Id)))
        {
            entry.IsHidden = false;
        }

        await _discovery.SaveAsync();

        // Nudges every view that listens for a settings change into rebuilding.
        await _settings.UpdateAsync(_ => { });
    }

    // ---- startup, tray and hotkey ----

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        if (!_startup.SetEnabled(value, StartMinimized))
        {
            StartupStatus = "Windows refused to update the startup entry.";
            return;
        }

        UpdateStartupStatus();
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = _settings.UpdateAsync(s => s.StartMinimized = value);

        // The Run entry embeds the switch, so it has to be rewritten when this changes.
        if (StartWithWindows)
        {
            _startup.SetEnabled(true, value);
        }
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.MinimizeToTray = value);
        }
    }

    partial void OnHideAfterLaunchChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.HideAfterLaunch = value);
        }
    }

    partial void OnHotkeyEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = _settings.UpdateAsync(s => s.HotkeyEnabled = value);

        _hotkeys.Apply(_settings.Current.Hotkey, value);
        UpdateHotkeyStatus();
    }

    [RelayCommand]
    private async Task ChangeHotkeyAsync()
    {
        HotkeyBinding? captured = await _dialogs.CaptureHotkeyAsync(_settings.Current.Hotkey);
        if (captured is null)
        {
            return;
        }

        await _settings.UpdateAsync(s => s.Hotkey = captured);

        HotkeyText = captured.ToString();
        _hotkeys.Apply(captured, HotkeyEnabled);
        UpdateHotkeyStatus();
    }

    /// <summary>Turns the registration result into something worth reading.</summary>
    private void UpdateHotkeyStatus() => HotkeyStatus = _hotkeys.Status switch
    {
        Services.HotkeyStatus.Active => "Active.",
        Services.HotkeyStatus.AlreadyInUse => "Another app already uses this combination. Pick a different one.",
        Services.HotkeyStatus.Invalid => "Add Ctrl, Alt or Shift - a hotkey needs at least one modifier.",
        Services.HotkeyStatus.Failed => "Windows would not register this combination.",
        _ => "Turned off.",
    };

    private void UpdateStartupStatus()
    {
        if (!StartWithWindows)
        {
            StartupStatus = "The launcher does not start with Windows.";
            return;
        }

        StartupStatus = _startup.IsStale()
            ? "The startup entry points at a different copy of the app. Turn this off and on again to repair it."
            : "The launcher starts with Windows.";
    }

    // ---- maintenance ----

    /// <summary>Re-runs discovery. User edits survive because entries are matched by id.</summary>
    [RelayCommand]
    private async Task RescanAsync()
    {
        if (_discovery.IsScanning)
        {
            return;
        }

        try
        {
            await _discovery.ScanAsync();
            IconCacheStatus = "Rescan complete.";
        }
        catch (Exception ex)
        {
            IconCacheStatus = "Rescan failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Deletes every cached PNG. The next scan re-extracts them, which is the fix when an
    /// app updates its icon but keeps the same executable timestamp.
    /// </summary>
    [RelayCommand]
    private async Task ClearIconCacheAsync()
    {
        int removed = await _icons.ClearAsync();

        IconCacheStatus = string.Format(
            CultureInfo.CurrentCulture,
            "Removed {0} cached icon(s). Rescan to rebuild them.",
            removed);
    }

    /// <summary>Writes settings, tabs, the catalog and the icon cache to one zip.</summary>
    [RelayCommand]
    private async Task ExportConfigurationAsync()
    {
        string suggested = string.Format(
            CultureInfo.InvariantCulture,
            "ybo-launcher-{0:yyyy-MM-dd}.zip",
            DateTime.Now);

        string? destination = await _dialogs.PickExportPathAsync(suggested);
        if (destination is null)
        {
            return;
        }

        ArchiveStatus = await _archive.ExportAsync(destination)
            ? "Exported to " + destination
            : "Export failed. Check that the folder is writable.";
    }

    /// <summary>
    /// Replaces everything from a zip, then reloads every service so the running app
    /// reflects the imported configuration without a restart.
    /// </summary>
    [RelayCommand]
    private async Task ImportConfigurationAsync()
    {
        string? source = await _dialogs.PickImportPathAsync();
        if (source is null)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            "Import configuration?",
            "This replaces your tabs, settings and app catalog. The current configuration is saved to a backup zip first, so you can undo this by importing that file.",
            "Import");

        if (!confirmed)
        {
            return;
        }

        ImportResult result = await _archive.ImportAsync(source);

        if (!result.Succeeded)
        {
            ArchiveStatus = "Import failed: " + result.Error;
            return;
        }

        await _settings.LoadAsync();
        await _tabs.LoadAsync();
        await _discovery.LoadCachedAsync();

        AppSettings current = _settings.Current;
        _theme.ApplyTheme(current.Theme);
        _theme.ApplyBackdrop(current.Backdrop);
        _hotkeys.Apply(current.Hotkey, current.HotkeyEnabled);

        ReloadFromSettings();

        ArchiveStatus = "Imported. Your previous configuration was saved to " + result.BackupPath;
    }

    /// <summary>Restores every preference. Apps, tabs and window geometry are untouched.</summary>
    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        bool confirmed = await _dialogs.ConfirmAsync(
            "Reset settings?",
            "Every preference goes back to its default. Your tabs, your apps and the window position are left alone.",
            "Reset settings");

        if (!confirmed)
        {
            return;
        }

        await _settings.UpdateAsync(s => s.ResetToDefaults());

        AppSettings current = _settings.Current;
        _theme.ApplyTheme(current.Theme);
        _theme.ApplyBackdrop(current.Backdrop);
        _hotkeys.Apply(current.Hotkey, current.HotkeyEnabled);

        ReloadFromSettings();
        IconCacheStatus = "Settings reset.";
    }

    /// <summary>Opens the state folder in File Explorer.</summary>
    [RelayCommand]
    private void OpenStorageFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.Root);
        }
        catch (Exception)
        {
            // Explorer will report the problem better than we can.
        }

        TryOpen(_paths.Root);
    }

    private static void TryOpen(string target)
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Opening a shell target is a convenience; never let it take the app down.
            Debug.WriteLine($"Could not open {target}: {ex}");
        }
    }
}
