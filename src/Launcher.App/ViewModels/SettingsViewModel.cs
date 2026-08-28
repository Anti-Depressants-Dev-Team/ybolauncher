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

namespace Launcher.App.ViewModels;

/// <summary>
/// Backs the Settings page. Only the settings the shell owns today are here; the rest
/// arrive with the phases that implement them (SPEC.md "Settings page").
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly StoragePaths _paths;
    private readonly IAppDiscoveryService _discovery;
    private readonly IIconService _icons;

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
    private string _iconCacheStatus = string.Empty;

    /// <summary>Suppresses write-back while the view model seeds itself from stored settings.</summary>
    private readonly bool _isInitializing;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        StoragePaths paths,
        IAppDiscoveryService discovery,
        IIconService icons)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _discovery = discovery;
        _icons = icons;

        // Seed the backing fields directly so the change handlers do not write back the
        // values we just read.
        _isInitializing = true;
        AppSettings current = _settings.Current;
        _selectedThemeIndex = (int)current.Theme;
        _selectedBackdropIndex = (int)current.Backdrop;
        _scanStartMenu = current.ScanStartMenu;
        _scanPackagedApps = current.ScanPackagedApps;
        _showFilteredEntries = current.ShowFilteredEntries;
        _showHiddenEntries = current.ShowHiddenEntries;
        _defaultViewModeIndex = (int)current.DefaultViewMode;
        _defaultTileScalePercent = current.DefaultTileScale * 100;
        _isInitializing = false;

        VersionDescription = BuildVersionDescription();
    }

    /// <summary>Folder holding settings.json, apps.json, tabs.json and the icon cache.</summary>
    public string StorageLocation => _paths.Root;

    public string StorageModeDescription => _paths.IsPortable
        ? "Portable mode: state is stored beside the executable because portable.txt is present."
        : "State is stored in your local application data folder.";

    public string VersionDescription { get; }

    public bool CanRescan => !_discovery.IsScanning;

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

    partial void OnShowHiddenEntriesChanged(bool value)
    {
        // The only way back for an app the user hid from a tile's context menu.
        if (!_isInitializing)
        {
            _ = _settings.UpdateAsync(s => s.ShowHiddenEntries = value);
        }
    }

    /// <summary>Re-runs discovery. User edits survive because entries are matched by id.</summary>
    [RelayCommand]
    private async Task RescanAsync()
    {
        if (_discovery.IsScanning)
        {
            return;
        }

        OnPropertyChanged(nameof(CanRescan));

        try
        {
            await _discovery.ScanAsync();
            IconCacheStatus = "Rescan complete.";
        }
        catch (Exception ex)
        {
            IconCacheStatus = "Rescan failed: " + ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(CanRescan));
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

    /// <summary>Opens the state folder in File Explorer.</summary>
    [RelayCommand]
    private void OpenStorageFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.Root);
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = _paths.Root,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Opening Explorer is a convenience; never let it take the app down.
            Debug.WriteLine($"Could not open {_paths.Root}: {ex}");
        }
    }
}
