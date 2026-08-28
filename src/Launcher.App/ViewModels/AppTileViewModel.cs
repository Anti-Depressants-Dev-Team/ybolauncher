using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Icons;
using Launcher.Core.Launching;
using Launcher.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Launcher.App.ViewModels;

/// <summary>One tile in the Home grid.</summary>
public sealed partial class AppTileViewModel : ObservableObject
{
    private readonly IIconService _icons;
    private readonly IAppTileHost _host;

    private BitmapImage? _icon;
    private bool _iconLoaded;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isHidden;

    public AppTileViewModel(
        AppEntry entry,
        TabViewModel owner,
        IIconService icons,
        ILaunchService launch,
        IAppTileHost host)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(launch);

        Entry = entry;
        Owner = owner;
        _icons = icons;
        _host = host;

        _displayName = entry.DisplayName;
        _isFavorite = entry.IsFavorite;
        _isHidden = entry.IsHidden;

        CanLaunchAsAdministrator = launch.CanLaunchAsAdministrator(entry);
        CanOpenFileLocation = launch.CanOpenFileLocation(entry);
        CanEditLaunchOptions = entry.LaunchKind == LaunchKind.Executable;
    }

    public AppEntry Entry { get; }

    /// <summary>The tab this tile is displayed in.</summary>
    public TabViewModel Owner { get; }

    /// <summary>Only tiles on a custom tab can be unpinned; Home membership is implicit.</summary>
    public bool CanUnpin => !Owner.IsHome;

    public bool CanLaunchAsAdministrator { get; }

    public bool CanOpenFileLocation { get; }

    /// <summary>Packaged apps and links have no command line to edit.</summary>
    public bool CanEditLaunchOptions { get; }

    public bool IsFiltered => Entry.IsFiltered;

    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(Entry.CustomIconPath);

    /// <summary>Tooltip text: what this tile will actually start.</summary>
    public string Detail => Entry.LaunchKind switch
    {
        LaunchKind.PackagedApp => Entry.AppUserModelId ?? "Packaged app",
        LaunchKind.Uri => Entry.LaunchUri ?? "Link",
        _ => Entry.TargetPath ?? "No target",
    };

    public string FavoriteMenuText => IsFavorite ? "Remove from favorites" : "Add to favorites";

    /// <summary>
    /// Hiding is reversible: hidden tiles reappear with the "Show hidden entries" setting,
    /// where this menu item offers to unhide them.
    /// </summary>
    public string HideMenuText => IsHidden ? "Show on Home" : "Hide from Home";

    /// <summary>
    /// Marks tiles that are only on screen because a "show" toggle is on, so they are not
    /// mistaken for ordinary entries.
    /// </summary>
    public bool ShowBadge => IsHidden || IsFiltered;

    /// <summary>Segoe Fluent Icons: HideBcc when hidden, Filter when filtered out.</summary>
    public string BadgeGlyph => IsHidden ? "" : "";

    public string BadgeTooltip => IsHidden
        ? "Hidden from Home"
        : "Filtered out as " + Entry.FilterReason;

    /// <summary>
    /// Decoded on first access rather than up front. The grid virtualizes, so with several
    /// hundred entries only the tiles actually scrolled into view pay for a bitmap.
    /// </summary>
    public BitmapImage? Icon
    {
        get
        {
            if (_iconLoaded)
            {
                return _icon;
            }

            _iconLoaded = true;
            _icon = LoadIcon();
            return _icon;
        }
    }

    /// <summary>Drops the decoded bitmap so the next access re-reads it from disk.</summary>
    public void ReloadIcon()
    {
        _iconLoaded = false;
        _icon = null;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasCustomIcon));
    }

    private BitmapImage? LoadIcon()
    {
        // A user-chosen icon wins over whatever discovery extracted.
        string? path = Entry.CustomIconPath is { Length: > 0 } custom && File.Exists(custom)
            ? custom
            : _icons.ResolveCachedPath(Entry.IconCacheFile);

        if (path is null)
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(path))
            {
                // Cached at 96px; tiles render at 48.
                DecodePixelWidth = 48,
                DecodePixelHeight = 48,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The GridViewItem container takes its automation name from the bound item, so
    /// without this Narrator announces the type name instead of the app.
    /// </summary>
    public override string ToString() => DisplayName;

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMenuText));

    partial void OnIsHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(HideMenuText));
        OnPropertyChanged(nameof(ShowBadge));
        OnPropertyChanged(nameof(BadgeGlyph));
        OnPropertyChanged(nameof(BadgeTooltip));
    }

    [RelayCommand]
    private Task LaunchAsync() => _host.LaunchAsync(this, asAdministrator: false);

    [RelayCommand]
    private Task LaunchAsAdministratorAsync() => _host.LaunchAsync(this, asAdministrator: true);

    [RelayCommand]
    private Task OpenFileLocationAsync() => _host.OpenFileLocationAsync(this);

    [RelayCommand]
    private Task RenameAsync() => _host.RenameAsync(this);

    [RelayCommand]
    private Task ChangeIconAsync() => _host.ChangeIconAsync(this);

    [RelayCommand]
    private Task ResetIconAsync() => _host.ResetIconAsync(this);

    [RelayCommand]
    private Task EditLaunchOptionsAsync() => _host.EditLaunchOptionsAsync(this);

    [RelayCommand]
    private Task ToggleFavoriteAsync() => _host.ToggleFavoriteAsync(this);

    [RelayCommand]
    private Task ToggleHiddenAsync() => _host.ToggleHiddenAsync(this);

    [RelayCommand]
    private Task ShowPropertiesAsync() => _host.ShowPropertiesAsync(this);

    /// <summary>Bound from the dynamically built "Pin to tab" submenu.</summary>
    [RelayCommand]
    private Task PinToTabAsync(string? tabId) =>
        string.IsNullOrWhiteSpace(tabId) ? Task.CompletedTask : _host.PinToTabAsync(this, tabId);

    [RelayCommand]
    private Task UnpinAsync() => _host.UnpinAsync(this);
}
