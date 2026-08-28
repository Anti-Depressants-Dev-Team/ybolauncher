using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Core;
using Launcher.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using WinUIEx;

namespace Launcher.App.Views;

/// <summary>
/// Shell window: custom title bar, Mica backdrop and the navigation frame.
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

    public MainWindow(ShellViewModel viewModel, ISettingsService settings, IThemeService theme)
    {
        ViewModel = viewModel;
        _settings = settings;
        _theme = theme;

        InitializeComponent();

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
        // Otherwise a first run that is never moved would leave no settings.json at all.
        AppWindow.Changed += OnAppWindowChanged;

        RestorePlacement();
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInteractiveRegions();
        AppTitleBar.Loaded += (_, _) => UpdateTitleBarInteractiveRegions();

        NavView.SelectedItem = NavView.MenuItems[0];
    }

    public ShellViewModel ViewModel { get; }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        Type pageType = tag switch
        {
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        }
    }

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
        Core.Models.WindowPlacement placement = _settings.Current.Window;

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
