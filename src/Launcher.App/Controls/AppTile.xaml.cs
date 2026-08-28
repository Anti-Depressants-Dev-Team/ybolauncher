using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Launcher.App.Controls;

/// <summary>
/// One app, rendered either as a grid tile or a compact list row. Which one is decided by
/// the owning tab's view mode, so a single definition - and a single context menu - serves
/// both and the two can never drift apart.
/// </summary>
public sealed partial class AppTile : UserControl
{
    public static readonly DependencyProperty TileProperty = DependencyProperty.Register(
        nameof(Tile),
        typeof(AppTileViewModel),
        typeof(AppTile),
        new PropertyMetadata(null));

    private readonly LibraryViewModel _library;

    public AppTile()
    {
        _library = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
    }

    public AppTileViewModel? Tile
    {
        get => (AppTileViewModel?)GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    private void OnPointerEnteredTile(object sender, PointerRoutedEventArgs e)
    {
        MoreButton.Opacity = 1;
        Motion.AnimateScale(TileRoot, Motion.HoverScale);
    }

    private void OnPointerExitedTile(object sender, PointerRoutedEventArgs e)
    {
        MoreButton.Opacity = 0;
        Motion.AnimateScale(TileRoot, 1.0);
    }

    /// <summary>
    /// Opens the same <see cref="MenuFlyout"/> that right-click uses, so the two can never
    /// offer different things.
    /// </summary>
    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (TileRoot.ContextFlyout is FlyoutBase flyout)
        {
            flyout.ShowAt(MoreButton);
        }
    }

    /// <summary>
    /// Fills in the "Pin to tab" submenu. The tab list changes at runtime and
    /// <c>MenuFlyoutSubItem.Items</c> is not bindable, so it is built on open.
    /// </summary>
    private void OnFlyoutOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout flyout || Tile is not { } tile)
        {
            return;
        }

        MenuFlyoutSubItem? submenu = flyout.Items
            .OfType<MenuFlyoutSubItem>()
            .FirstOrDefault(i => (i.Tag as string) == "pin");

        if (submenu is null)
        {
            return;
        }

        submenu.Items.Clear();

        foreach (TabViewModel tab in _library.Tabs)
        {
            // Home holds everything already, and pinning to the current tab is a no-op.
            if (tab.IsHome || ReferenceEquals(tab, tile.Owner))
            {
                continue;
            }

            submenu.Items.Add(new MenuFlyoutItem
            {
                Text = tab.Name,
                Command = tile.PinToTabCommand,
                CommandParameter = tab.Id,
            });
        }

        if (submenu.Items.Count == 0)
        {
            submenu.Items.Add(new MenuFlyoutItem { Text = "No other tabs yet", IsEnabled = false });
        }
    }
}
