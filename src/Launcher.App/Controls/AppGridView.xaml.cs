using System.Collections.Specialized;
using System.ComponentModel;
using Launcher.App.ViewModels;
using Launcher.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using VirtualKey = Windows.System.VirtualKey;

namespace Launcher.App.Controls;

/// <summary>
/// The apps for one tab, as a wrapping grid or a compact list, with drag-and-drop between
/// tabs and from Explorer.
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

        ApplyTransitions(TileGrid);
        ApplyTransitions(CompactList);
    }

    /// <summary>The tab whose contents this view shows.</summary>
    public TabViewModel? Tab
    {
        get => (TabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>The list actually on screen for the current view mode.</summary>
    private ListViewBase ActiveList => Tab?.IsListView == true ? CompactList : TileGrid;

    /// <summary>
    /// Fluent add/remove and reorder transitions, so tiles appearing after a scan or a
    /// drag animate in rather than popping. Skipped entirely under reduced motion.
    /// </summary>
    private static void ApplyTransitions(ListViewBase list)
    {
        if (!Motion.AnimationsEnabled)
        {
            list.ItemContainerTransitions = null;
            return;
        }

        list.ItemContainerTransitions =
        [
            new AddDeleteThemeTransition(),
            new ReorderThemeTransition(),
            new ContentThemeTransition(),
        ];
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
            previous.PropertyChanged -= OnTabPropertyChanged;
        }

        if (current is not null)
        {
            current.Items.CollectionChanged += OnItemsChanged;
            current.PropertyChanged += OnTabPropertyChanged;
        }

        ApplyViewMode();
        UpdateEmptyState();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.IsListView))
        {
            ApplyViewMode();
        }
    }

    /// <summary>Shows the control that matches the tab's view mode and hides the other.</summary>
    private void ApplyViewMode()
    {
        bool isList = Tab?.IsListView == true;

        TileGrid.ItemsSource = isList ? null : Tab?.Items;
        CompactList.ItemsSource = isList ? Tab?.Items : null;

        TileGrid.Visibility = isList ? Visibility.Collapsed : Visibility.Visible;
        CompactList.Visibility = isList ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The list performs the reorder itself and reports it as a Move. That is the only
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

        if (tab is null)
        {
            return;
        }

        EmptyStateTitle.Text = tab.EmptyStateTitle;
        EmptyStateBody.Text = tab.EmptyStateBody;
        // Segoe Fluent Icons: AllApps for an empty Home, Add for an empty custom tab.
        // Built from code points because an editor pass once ate the literal characters.
        EmptyStateGlyph.Glyph = char.ConvertFromUtf32(tab.IsHome ? 0xE71D : 0xE710);

        // Home has nowhere to send the user; an empty custom tab does.
        EmptyStateAction.Visibility = tab.IsHome ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnEmptyStateActionClick(object sender, RoutedEventArgs e) =>
        _library.SelectedTab = _library.Tabs.FirstOrDefault(t => t.IsHome);

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppTileViewModel tile)
        {
            _ = _library.LaunchAsync(tile, asAdministrator: false);
        }
    }

    /// <summary>
    /// Enter launches; Delete removes from a custom tab. Arrow keys are handled by the
    /// list itself.
    /// </summary>
    private void OnItemsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is not AppTileViewModel tile)
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
            // An internal drag: let the list run its own reorder logic untouched.
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

        // The deferral keeps the data view alive across the await.
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
}
