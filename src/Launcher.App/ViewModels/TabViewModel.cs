using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Launcher.App.ViewModels;

/// <summary>One tab in the strip, plus the tiles it shows.</summary>
public sealed partial class TabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _glyph;

    [ObservableProperty]
    private string? _accentColorHex;

    [ObservableProperty]
    private ViewMode _viewMode;

    [ObservableProperty]
    private double _tileScale = 1.0;

    /// <summary>
    /// Suppresses order persistence while the collection is being rebuilt. Without it a
    /// rebuild's Clear/Add churn would look like a manual reorder and overwrite the
    /// stored order with a half-populated list.
    /// </summary>
    public bool IsRebuilding { get; set; }

    public TabViewModel(LauncherTab model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        _name = model.Name;
        _glyph = model.Glyph;
        _accentColorHex = model.AccentColorHex;
        _viewMode = model.ViewMode;
        _tileScale = model.TileScale;
    }

    public LauncherTab Model { get; }

    public ObservableCollection<AppTileViewModel> Items { get; } = [];

    public string Id => Model.Id;

    public bool IsHome => Model.IsHome;

    /// <summary>Home has no close button; every other tab does.</summary>
    public bool CanClose => Model.CanBeDeleted;

    public bool HasGlyph => !string.IsNullOrWhiteSpace(Glyph);

    /// <summary>Null when the tab uses the system accent.</summary>
    public SolidColorBrush? AccentBrush => TryParseColor(AccentColorHex) is Color color
        ? new SolidColorBrush(color)
        : null;

    public bool HasAccent => AccentBrush is not null;

    /// <summary>
    /// Visibility rather than bool: these are bound from MainWindow, whose XAML root is a
    /// Window, and compiled-binding converters need a FrameworkElement lookup root.
    /// </summary>
    public Visibility GlyphVisibility => HasGlyph ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AccentVisibility => HasAccent ? Visibility.Visible : Visibility.Collapsed;

    public bool IsListView => ViewMode == ViewMode.CompactList;

    /// <summary>
    /// Unscaled tile metrics per view mode. A NaN width means "size to content", which is
    /// what the compact list wants so rows stretch across the panel.
    /// </summary>
    private (double Width, double Height, double Icon) BaseMetrics => ViewMode switch
    {
        ViewMode.LargeGrid => (152, 168, 72),
        ViewMode.CompactList => (double.NaN, 44, 28),
        _ => (112, 124, 48),
    };

    public double TileWidth => double.IsNaN(BaseMetrics.Width)
        ? double.NaN
        : Math.Round(BaseMetrics.Width * TileScale);

    public double TileHeight => Math.Round(BaseMetrics.Height * TileScale);

    public double IconSize => Math.Round(BaseMetrics.Icon * TileScale);

    // Layout is expressed as XAML types on the view model rather than through converters,
    // so one tile definition serves both the grid and the compact list.
    public Orientation ItemOrientation => IsListView ? Orientation.Horizontal : Orientation.Vertical;

    public HorizontalAlignment LabelAlignment =>
        IsListView ? HorizontalAlignment.Left : HorizontalAlignment.Center;

    public TextAlignment LabelTextAlignment => IsListView ? TextAlignment.Left : TextAlignment.Center;

    public VerticalAlignment ContentAlignment =>
        IsListView ? VerticalAlignment.Center : VerticalAlignment.Top;

    public int LabelMaxLines => IsListView ? 1 : 2;

    public Thickness ItemPadding => IsListView ? new Thickness(12, 4, 8, 4) : new Thickness(8, 10, 8, 8);

    public string EmptyStateTitle => IsHome ? "No apps found" : "This tab is empty";

    public string EmptyStateBody => IsHome
        ? "Nothing turned up in the Start Menu or the package catalog. Try Rescan, or check the discovery sources in Settings."
        : "Drag apps here from Home, drop files in from Explorer, or use \"Pin to tab\" on any tile.";

    /// <summary>The Home tab's automation and tooltip name.</summary>
    public override string ToString() => Name;

    partial void OnNameChanged(string value) => Model.Name = value;

    partial void OnGlyphChanged(string? value)
    {
        Model.Glyph = value;
        OnPropertyChanged(nameof(HasGlyph));
        OnPropertyChanged(nameof(GlyphVisibility));
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        Model.ViewMode = value;
        NotifyMetricsChanged();
        OnPropertyChanged(nameof(IsListView));
    }

    partial void OnTileScaleChanged(double value)
    {
        Model.TileScale = value;
        NotifyMetricsChanged();
    }

    private void NotifyMetricsChanged()
    {
        foreach (string property in new[]
        {
            nameof(TileWidth),
            nameof(TileHeight),
            nameof(IconSize),
            nameof(ItemOrientation),
            nameof(LabelAlignment),
            nameof(LabelTextAlignment),
            nameof(ContentAlignment),
            nameof(LabelMaxLines),
            nameof(ItemPadding),
        })
        {
            OnPropertyChanged(property);
        }
    }

    partial void OnAccentColorHexChanged(string? value)
    {
        Model.AccentColorHex = value;
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(HasAccent));
        OnPropertyChanged(nameof(AccentVisibility));
    }

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c>. Returns null for anything else.</summary>
    internal static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        ReadOnlySpan<char> digits = hex.AsSpan().Trim().TrimStart('#');

        if (digits.Length is not (6 or 8))
        {
            return null;
        }

        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return null;
        }

        byte alpha = digits.Length == 8 ? (byte)(value >> 24) : (byte)0xFF;

        return ColorHelper.FromArgb(
            alpha,
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value);
    }
}
