using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Core;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using WinUIEx;

namespace Launcher.App.Views;

/// <summary>
/// Shell window: custom title bar, Mica backdrop, the tab strip, and the Settings surface
/// that swaps in over it.
/// </summary>
public sealed partial class MainWindow : WindowEx
{
    private const int DefaultWidth = 1180;
    private const int DefaultHeight = 760;

    /// <summary>Idle delay before a move/resize is written to settings.json.</summary>
    private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly DispatcherQueueTimer _placementSaveTimer;

    public MainWindow(
        ShellViewModel viewModel,
        LibraryViewModel library,
        ISettingsService settings,
        IThemeService theme,
        IDialogService dialogs)
    {
        ViewModel = viewModel;
        Library = library;
        _settings = settings;
        _theme = theme;

        InitializeComponent();

        // Handed the window here rather than through the container: DialogService is
        // resolved by view models that are themselves built while this window is still
        // under construction, so taking MainWindow as a dependency would re-enter the
        // container mid-construction.
        dialogs.Attach(this);

        Title = AppInfo.ProductName;

        // Custom title bar: content is drawn under the caption area and AppTitleBar
        // becomes the drag region.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _theme.Attach(this, (FrameworkElement)Content);
        _theme.ApplyTheme(_settings.Current.Theme);
        _theme.ApplyBackdrop(_settings.Current.Backdrop);

        // Persisting on Closed would race the process shutdown, so geometry is saved
        // during the session instead, debounced to avoid a write per mouse move.
        _placementSaveTimer = DispatcherQueue.CreateTimer();
        _placementSaveTimer.Interval = PlacementSaveDelay;
        _placementSaveTimer.IsRepeating = false;
        _placementSaveTimer.Tick += (_, _) => SavePlacement();

        // Subscribe before restoring, so the initial placement is itself persisted.
        AppWindow.Changed += OnAppWindowChanged;

        RestorePlacement();
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInteractiveRegions();
        AppTitleBar.Loaded += (_, _) => UpdateTitleBarInteractiveRegions();

        SettingsFrame.Loaded += (_, _) =>
        {
            if (SettingsFrame.Content is null)
            {
                SettingsFrame.Navigate(typeof(SettingsPage));
            }
        };
    }

    public ShellViewModel ViewModel { get; }

    public LibraryViewModel Library { get; }

    // ---- tabs ----

    private async void OnAddTabClick(TabView sender, object args) =>
        await Library.AddTabCommand.ExecuteAsync(null);

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewModel tab)
        {
            // The view model confirms first, then deletes. The strip updates through the
            // service's TabsChanged event rather than by removing the item here, so a
            // cancelled confirmation leaves the tab exactly where it was.
            await Library.RequestDeleteTabAsync(tab);
        }
    }

    /// <summary>Persists a tab strip reorder done by dragging a tab header.</summary>
    private void OnTabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        // Ignore the churn from our own reconciliation, or it would be written straight
        // back as if the user had reordered.
        if (Library.IsSyncingTabs)
        {
            return;
        }

        _ = Library.ReorderTabsAsync([.. Library.Tabs.Select(t => t.Id)]);
    }

    private async void OnEditTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            await Library.EditTabAsync(tab);
        }
    }

    private async void OnDeleteTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            await Library.RequestDeleteTabAsync(tab);
        }
    }

    // ---- dropping apps onto a tab header ----

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel target }
            || !e.DataView.Properties.ContainsKey(DragFormats.EntryIds))
        {
            return;
        }

        string? sourceTabId = ReadSourceTabId(e);

        // Dropping back where it came from does nothing, so do not invite it.
        if (string.Equals(sourceTabId, target.Id, StringComparison.Ordinal))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        // From Home it is a copy - Home keeps everything. Out of a custom tab it is a move.
        bool fromHome = string.IsNullOrEmpty(sourceTabId)
            || string.Equals(sourceTabId, LauncherTab.HomeId, StringComparison.Ordinal);

        e.AcceptedOperation = fromHome ? DataPackageOperation.Copy : DataPackageOperation.Move;
        e.DragUIOverride.Caption = (fromHome ? "Add to " : "Move to ") + target.Name;
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void OnTabDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel target })
        {
            return;
        }

        if (!e.DataView.Properties.TryGetValue(DragFormats.EntryIds, out object? payload)
            || payload is not string joined)
        {
            return;
        }

        string[] ids = joined.Split(DragFormats.Separator, StringSplitOptions.RemoveEmptyEntries);
        string? sourceTabId = ReadSourceTabId(e);

        e.Handled = true;

        await Library.DropEntriesOnTabAsync(ids, sourceTabId, target.Id);
    }

    private static string? ReadSourceTabId(DragEventArgs e) =>
        e.DataView.Properties.TryGetValue(DragFormats.SourceTabId, out object? value)
            ? value as string
            : null;

    // ---- title bar and window placement ----

    /// <summary>
    /// Marks the search box as a passthrough region so clicks reach it instead of being
    /// treated as a title bar drag. Rects are in physical pixels, hence the scale factor.
    /// </summary>
    private void UpdateTitleBarInteractiveRegions()
    {
        if (Content?.XamlRoot is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        double scale = Content.XamlRoot.RasterizationScale;

        GeneralTransform transform = TitleBarSearchBox.TransformToVisual(null);
        Rect bounds = transform.TransformBounds(
            new Rect(0, 0, TitleBarSearchBox.ActualWidth, TitleBarSearchBox.ActualHeight));

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var passthrough = new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));

        InputNonClientPointerSource source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.SetRegionRects(NonClientRegionKind.Passthrough, [passthrough]);
    }

    private void RestorePlacement()
    {
        WindowPlacement placement = _settings.Current.Window;

        if (placement.HasValue)
        {
            var restored = new RectInt32(placement.Left, placement.Top, placement.Width, placement.Height);
            if (IsOnAVisibleDisplay(restored))
            {
                AppWindow.MoveAndResize(restored);
            }
            else
            {
                // The monitor it was last on is gone; fall back to a centred default.
                ResizeAndCenter();
            }
        }
        else
        {
            ResizeAndCenter();
        }

        if (placement.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void ResizeAndCenter()
    {
        AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));

        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = display.WorkArea;

        AppWindow.Move(new PointInt32(
            work.X + ((work.Width - AppWindow.Size.Width) / 2),
            work.Y + ((work.Height - AppWindow.Size.Height) / 2)));
    }

    /// <summary>
    /// Guards against restoring onto a monitor that has since been disconnected, which
    /// would leave the window invisible off-screen.
    /// </summary>
    private static bool IsOnAVisibleDisplay(RectInt32 rect)
    {
        DisplayArea display = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest);
        RectInt32 work = display.WorkArea;

        int overlapX = Math.Min(rect.X + rect.Width, work.X + work.Width) - Math.Max(rect.X, work.X);
        int overlapY = Math.Min(rect.Y + rect.Height, work.Y + work.Height) - Math.Max(rect.Y, work.Y);

        // Require a reasonable slice of the title bar to be reachable with the mouse.
        return overlapX > 120 && overlapY > 40;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange)
        {
            _placementSaveTimer.Start();
        }
    }

    private void SavePlacement()
    {
        bool isMaximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;

        _ = _settings.UpdateAsync(s =>
        {
            s.Window.IsMaximized = isMaximized;

            // Keep the restore geometry from before maximizing, so unmaximizing on the
            // next launch returns the window to a sensible size.
            if (!isMaximized)
            {
                s.Window.Left = position.X;
                s.Window.Top = position.Y;
                s.Window.Width = size.Width;
                s.Window.Height = size.Height;
            }
        });
    }
}
