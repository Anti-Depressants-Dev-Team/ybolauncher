using System.Runtime.Versioning;
using Launcher.Core.Icons;
using Launcher.Core.Interop;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Discovery;

/// <summary>
/// Walks both Start Menu Programs folders and turns every shortcut into an entry.
/// <para>
/// The whole walk runs on one STA thread because <c>IShellLink</c> and the shell image
/// factory are apartment-threaded; see <see cref="StaThread"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartMenuAppSource : IAppSource
{
    private readonly ShellLinkResolver _resolver;
    private readonly IIconService _icons;
    private readonly ILogger<StartMenuAppSource> _logger;

    public StartMenuAppSource(
        ShellLinkResolver resolver,
        IIconService icons,
        ILogger<StartMenuAppSource>? logger = null)
    {
        _resolver = resolver;
        _icons = icons;
        _logger = logger ?? NullLogger<StartMenuAppSource>.Instance;
    }

    public AppSource Kind => AppSource.StartMenu;

    public string DisplayName => "Start Menu";

    /// <summary>The machine-wide and per-user Programs folders, in that order.</summary>
    public static IReadOnlyList<string> GetRoots()
    {
        var roots = new List<string>(2);

        foreach (Environment.SpecialFolder folder in
                 new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            try
            {
                string path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    roots.Add(path);
                }
            }
            catch (Exception)
            {
                // A shell folder we cannot resolve simply is not scanned.
            }
        }

        return roots;
    }

    public Task<IReadOnlyList<AppEntry>> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return StaThread.RunAsync<IReadOnlyList<AppEntry>>(
            () => Scan(context, cancellationToken),
            cancellationToken);
    }

    private List<AppEntry> Scan(DiscoveryContext context, CancellationToken cancellationToken)
    {
        List<string> files = CollectShortcutFiles(cancellationToken);
        var entries = new List<AppEntry>(files.Count);

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AppEntry? entry = BuildEntry(files[i], context.IconPixelSize);
            if (entry is not null)
            {
                entries.Add(entry);
            }

            // Reporting every item would flood the UI thread; once per 25 is plenty.
            if (i % 25 == 0 || i == files.Count - 1)
            {
                context.Progress?.Report(new DiscoveryProgress(DisplayName, i + 1, files.Count));
            }
        }

        _logger.LogInformation("Start Menu scan produced {Count} entries from {Files} files.", entries.Count, files.Count);
        return entries;
    }

    private List<string> CollectShortcutFiles(CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,

            // Skip folders we cannot read rather than throwing partway through the walk.
            IgnoreInaccessible = true,

            // Following reparse points risks walking into a loop or onto a slow network path.
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            MaxRecursionDepth = 12,
        };

        var files = new List<string>();

        foreach (string root in GetRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string pattern in new[] { "*.lnk", "*.url" })
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(root, pattern, options));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not enumerate {Pattern} under {Root}.", pattern, root);
                }
            }
        }

        return files;
    }

    private AppEntry? BuildEntry(string path, int iconPixelSize)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            AppEntry entry = path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                ? BuildInternetShortcut(path, name)
                : BuildShellLink(path, name);

            entry.MergeKey = AppIdentity.ForEntry(entry);
            entry.Id = AppIdentity.ToId(entry.MergeKey);

            // Extract from the .lnk rather than its target: the shell then honours the
            // shortcut's own icon location and index, which a target-only lookup loses.
            entry.IconCacheFile = _icons.ExtractFromPath(path, iconPixelSize);

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping shortcut {Path}.", path);
            return null;
        }
    }

    private AppEntry BuildShellLink(string path, string name)
    {
        ShortcutTarget? target = _resolver.Resolve(path);

        var entry = new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            ShortcutPath = path,
        };

        if (target is null)
        {
            entry.LaunchKind = LaunchKind.Executable;
            return entry;
        }

        entry.Arguments = target.Arguments;
        entry.WorkingDirectory = target.WorkingDirectory;
        entry.TargetPath = string.IsNullOrWhiteSpace(target.TargetPath) ? null : target.TargetPath;

        // Only a genuine packaged AUMID makes this a Store app launcher, and recording it
        // is what merges the shortcut with its package catalog entry. Plain desktop apps
        // also stamp an AUMID on their shortcuts purely for taskbar grouping - see
        // PackagedAppId - and those must keep launching by path.
        if (PackagedAppId.IsPackagedAumid(target.AppUserModelId))
        {
            entry.LaunchKind = LaunchKind.PackagedApp;
            entry.AppUserModelId = target.AppUserModelId;
        }
        else
        {
            entry.LaunchKind = LaunchKind.Executable;

            // Some Start Menu items - File Explorer, Control Panel, Run - are shortcuts to
            // a shell folder rather than a file, so IShellLink::GetPath returns nothing.
            // Launching the .lnk itself resolves the ID list for us, which beats either
            // dropping these (they are real apps) or reimplementing pidl launching.
            entry.TargetPath ??= path;
        }

        return entry;
    }

    /// <summary>
    /// Reads a .url internet shortcut. These are INI files; only the URL matters.
    /// They are kept (and marked as web links by the filter) so the "show filtered
    /// entries" setting can reveal them.
    /// </summary>
    private AppEntry BuildInternetShortcut(string path, string name)
    {
        string? url = null;

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    url = line[4..].Trim();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read internet shortcut {Path}.", path);
        }

        return new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            ShortcutPath = path,
            LaunchKind = LaunchKind.Uri,
            LaunchUri = url,
        };
    }
}
