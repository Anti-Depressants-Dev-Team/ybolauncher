using Launcher.Core.Icons;
using Launcher.Core.Models;
using Launcher.Core.Search;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Launcher.App.ViewModels;

/// <summary>One row in the search results list.</summary>
public sealed class SearchResultViewModel
{
    private readonly IIconService _icons;

    private BitmapImage? _icon;
    private bool _iconLoaded;

    public SearchResultViewModel(SearchResult result, IIconService icons)
    {
        ArgumentNullException.ThrowIfNull(result);

        Entry = result.Entry;
        _icons = icons;

        // When only the executable's file name matched there is nothing in the visible
        // name to highlight, so fall back to a single unhighlighted run.
        NameSegments = result.NameMatch is { } match
            ? match.ToSegments(Entry.DisplayName)
            : [new TextSegment(Entry.DisplayName, false)];
    }

    public AppEntry Entry { get; }

    /// <summary>The display name split into matched and unmatched runs.</summary>
    public IReadOnlyList<TextSegment> NameSegments { get; }

    public string Detail => Entry.LaunchKind switch
    {
        LaunchKind.PackagedApp => Entry.AppUserModelId ?? "Packaged app",
        LaunchKind.Uri => Entry.LaunchUri ?? "Link",
        _ => Entry.TargetPath ?? "No target",
    };

    /// <summary>Narrator reads the container's name from this.</summary>
    public override string ToString() => Entry.DisplayName;

    /// <summary>Decoded on first access; the results list virtualizes.</summary>
    public BitmapImage? Icon
    {
        get
        {
            if (_iconLoaded)
            {
                return _icon;
            }

            _iconLoaded = true;

            string? path = Entry.CustomIconPath is { Length: > 0 } custom && File.Exists(custom)
                ? custom
                : _icons.ResolveCachedPath(Entry.IconCacheFile);

            if (path is null)
            {
                return null;
            }

            try
            {
                _icon = new BitmapImage(new Uri(path))
                {
                    DecodePixelWidth = 32,
                    DecodePixelHeight = 32,
                };
            }
            catch (Exception)
            {
                _icon = null;
            }

            return _icon;
        }
    }
}
