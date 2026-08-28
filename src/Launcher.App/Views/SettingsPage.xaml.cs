using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.App.Views;

/// <summary>
/// Settings page. Grows with each phase; see SPEC.md "Settings page" for the full list.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        // The Frame constructs pages parameterlessly, so the view model is pulled from
        // the container here rather than injected.
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
