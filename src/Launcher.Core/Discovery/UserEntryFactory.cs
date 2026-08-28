using System.Runtime.Versioning;
using Launcher.Core.Icons;
using Launcher.Core.Interop;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Discovery;

/// <summary>
/// Builds catalog entries for things the user drags in from Explorer.
/// <para>
/// Dropped shortcuts are resolved rather than stored as-is, so dropping a <c>.lnk</c> for
/// an app that discovery already found produces the same merge key and reuses the existing
/// entry instead of creating a duplicate tile.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UserEntryFactory
{
    private readonly ShellLinkResolver _resolver;
    private readonly IIconService _icons;
    private readonly ILogger<UserEntryFactory> _logger;

    public UserEntryFactory(
        ShellLinkResolver resolver,
        IIconService icons,
        ILogger<UserEntryFactory>? logger = null)
    {
        _resolver = resolver;
        _icons = icons;
        _logger = logger ?? NullLogger<UserEntryFactory>.Instance;
    }

    /// <summary>
    /// Creates entries for the dropped paths. Everything runs on one STA thread because
    /// shortcut resolution and icon extraction both need apartment-threaded shell COM.
    /// </summary>
    public Task<IReadOnlyList<AppEntry>> CreateAsync(
        IEnumerable<string> paths,
        int iconPixelSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] snapshot = [.. paths];

        return StaThread.RunAsync<IReadOnlyList<AppEntry>>(
            () =>
            {
                var entries = new List<AppEntry>(snapshot.Length);

                foreach (string path in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    AppEntry? entry = Create(path, iconPixelSize);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }

                return entries;
            },
            cancellationToken);
    }

    private AppEntry? Create(string path, int iconPixelSize)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            bool isDirectory = Directory.Exists(path);

            if (!isDirectory && !File.Exists(path))
            {
                return null;
            }

            AppEntry entry = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                ? FromShortcut(path)
                : FromPath(path, isDirectory);

            entry.MergeKey = AppIdentity.ForEntry(entry);
            entry.Id = AppIdentity.ToId(entry.MergeKey);
            entry.IconCacheFile = _icons.ExtractFromPath(path, iconPixelSize);

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not create an entry for {Path}.", path);
            return null;
        }
    }

    private AppEntry FromShortcut(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        ShortcutTarget? target = _resolver.Resolve(path);

        var entry = new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.UserAdded,
            ShortcutPath = path,
            LaunchKind = LaunchKind.Executable,
            TargetPath = path,
        };

        if (target is null)
        {
            return entry;
        }

        entry.Arguments = target.Arguments;
        entry.WorkingDirectory = target.WorkingDirectory;

        if (PackagedAppId.IsPackagedAumid(target.AppUserModelId))
        {
            entry.LaunchKind = LaunchKind.PackagedApp;
            entry.AppUserModelId = target.AppUserModelId;
            entry.TargetPath = null;
        }
        else if (!string.IsNullOrWhiteSpace(target.TargetPath))
        {
            entry.TargetPath = target.TargetPath;
        }

        return entry;
    }

    private static AppEntry FromPath(string path, bool isDirectory)
    {
        string name = isDirectory
            ? new DirectoryInfo(path).Name
            : Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        return new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.UserAdded,
            LaunchKind = LaunchKind.Executable,
            TargetPath = path,
        };
    }
}
