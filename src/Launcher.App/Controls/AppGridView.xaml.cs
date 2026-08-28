using System.Collections.Specialized;
using Launcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Launcher.App.Controls;

/// <summary>
/// The tile grid for one tab, with drag-and-drop between tabs and from Explorer.
/// </summary>
public sealed partial class AppGridView : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab),
        typeof(TabViewModel),
        typeof(AppGridView),
        new PropertyMetadata(null, OnTabPropertyChanged));

    private readonly LibraryViewModel _library;

    public AppGridView()
    {
        _library = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
    }

    /// <summary>The tab whose contents this grid shows.</summary>
    public TabViewModel? Tab
    {
        get => (TabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private static void OnTabPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is AppGridView view)
        {
            view.AttachTab(args.OldValue as TabViewModel, args.NewValue as TabViewModel);
        }
    }

    private void AttachTab(TabViewModel? previous, TabViewModel? current)
    {
        if (previous is not null)
        {
            previous.Items.CollectionChanged -= OnItemsChanged;
        }

        if (current is not null)
        {
            current.Items.CollectionChanged += OnItemsChanged;
        }

        Grid.ItemsSource = current?.Items;
        UpdateEmptyState();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // GridView performs the reorder itself and reports it as a Move. That is the only
        // signal that the user actually dragged something, as opposed to a rebuild.
        if (e.Action == NotifyCollectionChangedAction.Move && Tab is { IsRebuilding: false } tab)
        {
            _ = _library.PersistOrderAsync(tab);
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        TabViewModel? tab = Tab;

        bool empty = tab is null || tab.Items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (tab is not null)
        {
            EmptyStateTitle.Text = tab.EmptyStateTitle;
            EmptyStateBody.Text = tab.EmptyStateBody;
            EmptyStateGlyph.Glyph = tab.IsHome ? "" : "";
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppTileViewModel tile)
        {
            _ = _library.LaunchAsync(tile, asAdministrator: false);
        }
    }

    /// <summary>Enter launches; Delete removes from a custom tab. Full keyboard nav is Phase 6.</summary>
    private void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Grid.SelectedItem is not AppTileViewModel tile)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                _ = _library.LaunchAsync(tile, asAdministrator: false);
                break;

            case VirtualKey.Delete when tile.CanUnpin:
                // Removes from this tab only - never uninstalls, never touches Home.
                e.Handled = true;
                _ = _library.UnpinAsync(tile);
                break;

            default:
                break;
        }
    }

    // ---- drag and drop ----

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var ids = e.Items.OfType<AppTileViewModel>().Select(t => t.Entry.Id).ToList();

        if (ids.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties[DragFormats.EntryIds] = string.Join(DragFormats.Separator, ids);
        e.Data.Properties[DragFormats.SourceTabId] = Tab?.Id ?? string.Empty;

        // Copy for Home (which keeps everything), Move for a custom tab.
        e.Data.RequestedOperation = Tab?.IsHome == true
            ? DataPackageOperation.Copy
            : DataPackageOperation.Move;

        e.Data.SetText(string.Join(
            Environment.NewLine,
            e.Items.OfType<AppTileViewModel>().Select(t => t.DisplayName)));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            // An internal drag: let GridView run its own reorder logic untouched.
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Tab is null ? "Add" : "Add to " + Tab.Name;
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || Tab is null)
        {
            return;
        }

        // Taken before the first await: the deferral keeps the data view alive, but the
        // event args must be consumed synchronously.
        DragOperationDeferral deferral = e.GetDeferral();

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();

            var paths = items
                .Select(i => i.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            await _library.AddDroppedPathsAsync(paths, Tab);
        }
        catch (Exception)
        {
            // A drop that cannot be read is not worth crashing over; the library reports
            // anything it could not turn into an entry.
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---- context menu ----

    /// <summary>
    /// Fills in the "Pin to tab" submenu. The tab list changes at runtime and
    /// <c>MenuFlyoutSubItem.Items</c> is not bindable, so it is built on open.
    /// </summary>
    private void OnTileFlyoutOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout flyout
            || flyout.Target?.DataContext is not AppTileViewModel tile)
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
            submenu.Items.Add(new MenuFlyoutItem
            {
                Text = "No other tabs yet",
                IsEnabled = false,
            });
        }
    }

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
