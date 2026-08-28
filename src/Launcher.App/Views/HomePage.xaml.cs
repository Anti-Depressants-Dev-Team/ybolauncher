using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Launcher.App.Views;

/// <summary>Home: the virtualized grid of every discovered app.</summary>
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        // The view model is a singleton so the catalog survives navigating away and back.
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppTileViewModel tile)
        {
            _ = ViewModel.LaunchAsync(tile, asAdministrator: false);
        }
    }

    /// <summary>Enter launches the focused tile. Full keyboard navigation is Phase 6.</summary>
    private void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (AppGrid.SelectedItem is AppTileViewModel tile)
        {
            e.Handled = true;
            _ = ViewModel.LaunchAsync(tile, asAdministrator: false);
        }
    }

    /// <summary>
    /// Opens the tile's context menu from the "..." button, reusing the same
    /// <see cref="MenuFlyout"/> that right-click uses so the two can never drift apart.
    /// </summary>
    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button)
        {
            return;
        }

        for (DependencyObject? node = button; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { ContextFlyout: FlyoutBase flyout })
            {
                flyout.ShowAt(button);
                return;
            }
        }
    }

    private void OnTilePointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetMoreButtonOpacity(sender, 1);

    private void OnTilePointerExited(object sender, PointerRoutedEventArgs e) =>
        SetMoreButtonOpacity(sender, 0);

    /// <summary>
    /// Each templated tile has its own name scope, so the button is looked up from the
    /// template root rather than by field.
    /// </summary>
    private static void SetMoreButtonOpacity(object sender, double opacity)
    {
        if (sender is FrameworkElement root && root.FindName("MoreButton") is UIElement button)
        {
            button.Opacity = opacity;
        }
    }
}
