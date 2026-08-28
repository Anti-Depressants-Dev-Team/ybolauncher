using Launcher.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Launcher.App.ViewModels;

/// <summary>
/// Display wrapper around an <see cref="AppEntry"/> for the Phase 2 proof-of-discovery
/// list. Phase 3 replaces this with the real tile view model.
/// </summary>
public sealed class AppListItem(AppEntry entry, string? iconPath)
{
    private BitmapImage? _icon;
    private bool _iconLoadAttempted;

    public AppEntry Entry { get; } = entry;

    public string DisplayName => Entry.DisplayName;

    /// <summary>What the entry will actually launch, shown so the scan can be eyeballed.</summary>
    public string Detail => Entry.LaunchKind switch
    {
        LaunchKind.PackagedApp => Entry.AppUserModelId ?? "packaged app",
        LaunchKind.Uri => Entry.LaunchUri ?? "uri",
        _ => Entry.TargetPath ?? "(no target)",
    };

    public string SourceLabel => Entry.Source switch
    {
        AppSource.Packaged => "Packaged",
        AppSource.StartMenu => "Start Menu",
        AppSource.UserAdded => "Added",
        _ => Entry.Source.ToString(),
    };

    /// <summary>Set for entries the junk filter rejected, shown only when they are revealed.</summary>
    public string? FilteredLabel => Entry.IsFiltered ? Entry.FilterReason.ToString() : null;

    public bool IsFiltered => Entry.IsFiltered;

    /// <summary>
    /// Decoded lazily. The list virtualizes, so only items actually scrolled into view
    /// pay for a bitmap - with several hundred entries, eager decoding would be wasteful.
    /// </summary>
    public BitmapImage? Icon
    {
        get
        {
            if (_iconLoadAttempted)
            {
                return _icon;
            }

            _iconLoadAttempted = true;

            if (iconPath is null)
            {
                return null;
            }

            try
            {
                _icon = new BitmapImage(new Uri(iconPath))
                {
                    // The cache stores icons at 96px; the list only needs 32.
                    DecodePixelWidth = 32,
                    DecodePixelHeight = 32,
                };
            }
            catch (Exception)
            {
                // A missing or corrupt cache file just means no icon for this row.
                _icon = null;
            }

            return _icon;
        }
    }
}
