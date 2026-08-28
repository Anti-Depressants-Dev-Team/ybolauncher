using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.App.Views;

/// <summary>
/// Phase 2 discovery list. Phase 3 replaces it with the virtualized tile grid.
/// </summary>
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        // The view model is a singleton so the catalog survives navigating away and back.
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }
}
