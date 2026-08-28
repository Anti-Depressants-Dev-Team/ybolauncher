using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>
/// Backs the shell window chrome: title bar text and the selected navigation section.
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
}
