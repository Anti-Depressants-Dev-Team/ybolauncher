using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
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

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedBackdropIndex;

    /// <summary>Suppresses write-back while the view model seeds itself from stored settings.</summary>
    private readonly bool _isInitializing;

    public SettingsViewModel(ISettingsService settings, IThemeService theme, StoragePaths paths)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;

        // Seed the backing fields directly so the change handlers do not write back the
        // values we just read.
        _isInitializing = true;
        _selectedThemeIndex = (int)_settings.Current.Theme;
        _selectedBackdropIndex = (int)_settings.Current.Backdrop;
        _isInitializing = false;

        VersionDescription = BuildVersionDescription();
    }

    /// <summary>Folder holding settings.json, apps.json, tabs.json and the icon cache.</summary>
    public string StorageLocation => _paths.Root;

    public string StorageModeDescription => _paths.IsPortable
        ? "Portable mode: state is stored beside the executable because portable.txt is present."
        : "State is stored in your local application data folder.";

    public string VersionDescription { get; }

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
