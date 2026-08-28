using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Microsoft.UI.Xaml;

namespace Launcher.App.ViewModels;

/// <summary>
/// Backs the shell window chrome: title bar text, and whether Settings is covering the
/// tab strip.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = AppInfo.ProductName;

    /// <summary>
    /// Placeholder shown in the title bar search box. The box is inert until Phase 5
    /// wires up the fuzzy matcher.
    /// </summary>
    [ObservableProperty]
    private string _searchPlaceholder = "Search apps";

    /// <summary>
    /// True while the Settings surface covers the tab strip. Settings swaps the whole
    /// content area rather than living in a tab, so the strip stays entirely user-owned.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>
    /// Exposed as <see cref="Visibility"/> rather than through a converter: MainWindow's
    /// XAML root is a Window, and the compiled-binding converter lookup root has to be a
    /// FrameworkElement, so <c>{x:Bind ... Converter=...}</c> does not compile there.
    /// </summary>
    public Visibility TabsVisibility => IsSettingsOpen ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SettingsVisibility => IsSettingsOpen ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsSettingsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(TabsVisibility));
        OnPropertyChanged(nameof(SettingsVisibility));
    }

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;
}
