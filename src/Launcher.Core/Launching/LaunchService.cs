using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Management.Deployment;

namespace Launcher.Core.Launching;

/// <inheritdoc cref="ILaunchService"/>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class LaunchService : ILaunchService
{
    /// <summary>ERROR_CANCELLED - the user dismissed the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    private readonly ILogger<LaunchService> _logger;

    public LaunchService(ILogger<LaunchService>? logger = null) =>
        _logger = logger ?? NullLogger<LaunchService>.Instance;

    public bool CanLaunchAsAdministrator(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.LaunchKind == LaunchKind.Executable && !string.IsNullOrWhiteSpace(entry.TargetPath);
    }

    public bool CanOpenFileLocation(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return !string.IsNullOrWhiteSpace(entry.TargetPath)
            || !string.IsNullOrWhiteSpace(entry.ShortcutPath);
    }

    public async Task<LaunchResult> LaunchAsync(
        AppEntry entry,
        bool asAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        LaunchResult result = entry.LaunchKind switch
        {
            LaunchKind.PackagedApp => await LaunchPackagedAsync(entry).ConfigureAwait(false),
            LaunchKind.Uri => LaunchUri(entry),
            _ => LaunchExecutable(entry, asAdministrator),
        };

        if (result.Succeeded)
        {
            entry.LaunchCount++;
            entry.LastLaunchedUtc = DateTimeOffset.UtcNow;
        }

        return result;
    }

    private LaunchResult LaunchExecutable(AppEntry entry, bool asAdministrator)
    {
        if (string.IsNullOrWhiteSpace(entry.TargetPath))
        {
            return LaunchResult.Failed("This entry has no target to launch.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = entry.TargetPath,

                // Required for shortcut and document targets, and for "runas".
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDirectory(entry),
            };

            if (!string.IsNullOrWhiteSpace(entry.Arguments))
            {
                startInfo.Arguments = entry.Arguments;
            }

            if (asAdministrator)
            {
                startInfo.Verb = "runas";
            }

            using Process? process = Process.Start(startInfo);
            return LaunchResult.Success();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user clicked No on the elevation prompt. Deliberate, not a failure.
            return LaunchResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not launch {Name} ({Path}).", entry.DisplayName, entry.TargetPath);
            return LaunchResult.Failed(Describe(ex));
        }
    }

    private LaunchResult LaunchUri(AppEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.LaunchUri))
        {
            return LaunchResult.Failed("This entry has no link to open.");
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = entry.LaunchUri,
                UseShellExecute = true,
            });

            return LaunchResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Uri}.", entry.LaunchUri);
            return LaunchResult.Failed(Describe(ex));
        }
    }

    /// <summary>
    /// Starts a packaged app through the package catalog, as SPEC.md requires - never by
    /// path. The AppListEntry is re-found by package family name, which is a targeted
    /// lookup rather than a full catalog enumeration.
    /// </summary>
    private async Task<LaunchResult> LaunchPackagedAsync(AppEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AppUserModelId))
        {
            return LaunchResult.Failed("This entry has no application id to launch.");
        }

        try
        {
            AppListEntry? match = await FindAppListEntryAsync(entry).ConfigureAwait(false);

            if (match is not null)
            {
                return await match.LaunchAsync()
                    ? LaunchResult.Success()
                    : LaunchResult.Failed("Windows declined to start this app.");
            }

            // The package may have been removed since the last scan, or the catalog lookup
            // may be unavailable. The AppsFolder shell namespace still resolves an AUMID.
            _logger.LogInformation(
                "No AppListEntry for {Aumid}; falling back to the AppsFolder shell path.",
                entry.AppUserModelId);

            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + entry.AppUserModelId,
                UseShellExecute = true,
            });

            return LaunchResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not launch packaged app {Aumid}.", entry.AppUserModelId);
            return LaunchResult.Failed(Describe(ex));
        }
    }

    private static async Task<AppListEntry?> FindAppListEntryAsync(AppEntry entry)
    {
        var manager = new PackageManager();

        IEnumerable<Package> packages = string.IsNullOrWhiteSpace(entry.PackageFamilyName)
            ? manager.FindPackagesForUser(string.Empty)
            : manager.FindPackagesForUser(string.Empty, entry.PackageFamilyName);

        foreach (Package package in packages)
        {
            IReadOnlyList<AppListEntry> appEntries;

            try
            {
                appEntries = await package.GetAppListEntriesAsync();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (AppListEntry candidate in appEntries)
            {
                if (string.Equals(candidate.AppUserModelId, entry.AppUserModelId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public Task<LaunchResult> OpenFileLocationAsync(
        AppEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Prefer the real target; fall back to the shortcut when the target is gone, so
        // the user can still see what the entry points at.
        string? path = FirstExisting(entry.TargetPath, entry.ShortcutPath);

        if (path is null)
        {
            return Task.FromResult(LaunchResult.Failed("There is no file on disk for this entry."));
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",

                // /select, needs the path quoted; a bare path would open the file instead.
                Arguments = string.Format(CultureInfo.InvariantCulture, "/select,\"{0}\"", path),
                UseShellExecute = true,
            });

            return Task.FromResult(LaunchResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reveal {Path}.", path);
            return Task.FromResult(LaunchResult.Failed(Describe(ex)));
        }
    }

    private static string? FirstExisting(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // An unreadable path simply is not a candidate.
            }
        }

        return null;
    }

    /// <summary>
    /// The shortcut's start-in directory when it is usable, otherwise the target's own
    /// folder. Some apps refuse to start from the wrong working directory.
    /// </summary>
    private static string ResolveWorkingDirectory(AppEntry entry)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory)
                && Directory.Exists(entry.WorkingDirectory))
            {
                return entry.WorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(entry.TargetPath))
            {
                string? folder = Path.GetDirectoryName(entry.TargetPath);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    return folder;
                }
            }
        }
        catch (Exception)
        {
            // Fall through to letting the shell decide.
        }

        return string.Empty;
    }

    /// <summary>Turns an exception into something worth putting in an InfoBar.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException => "The file this entry points at no longer exists.",
        DirectoryNotFoundException => "The folder this entry points at no longer exists.",
        UnauthorizedAccessException => "Windows denied access to this file.",
        Win32Exception win32 => win32.Message,
        _ => ex.Message,
    };
}
