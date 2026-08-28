using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel;

namespace Launcher.Core.Discovery;

/// <inheritdoc cref="IAppWatcherService"/>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class AppWatcherService : IAppWatcherService
{
    /// <summary>
    /// Quiet period before a burst of changes is reported. An installer writes a folder of
    /// shortcuts in quick succession, and the package catalog fires repeatedly during one
    /// install; rescanning per event would be pointless work.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(4);

    private readonly ILogger<AppWatcherService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _gate = new();

    private Timer? _settleTimer;
    private PackageCatalog? _catalog;
    private bool _disposed;

    public AppWatcherService(ILogger<AppWatcherService>? logger = null) =>
        _logger = logger ?? NullLogger<AppWatcherService>.Instance;

    public bool IsWatching { get; private set; }

    public event EventHandler? ChangeDetected;

    public void StartWatching()
    {
        lock (_gate)
        {
            if (IsWatching || _disposed)
            {
                return;
            }

            StartFileWatchers();
            StartPackageCatalog();

            IsWatching = _watchers.Count > 0 || _catalog is not null;
        }
    }

    private void StartFileWatchers()
    {
        foreach (string root in StartMenuAppSource.GetRoots())
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,

                    // The default 8KB buffer overflows easily when an installer writes a
                    // whole folder; an overflow drops events silently.
                    InternalBufferSize = 64 * 1024,
                };

                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileChanged;
                watcher.Changed += OnFileChanged;
                watcher.Error += OnWatcherError;

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // A folder we cannot watch just means manual Rescan for that source.
                _logger.LogWarning(ex, "Could not watch {Root}.", root);
            }
        }
    }

    private void StartPackageCatalog()
    {
        try
        {
            _catalog = PackageCatalog.OpenForCurrentUser();

            _catalog.PackageInstalling += OnPackageInstalling;
            _catalog.PackageUninstalling += OnPackageUninstalling;
            _catalog.PackageUpdating += OnPackageUpdating;
        }
        catch (Exception ex)
        {
            // Losing Store app notifications is survivable; Start Menu watching continues.
            _logger.LogWarning(ex, "Could not open the package catalog for change notifications.");
            _catalog = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Only shortcuts matter, and only their creation or removal. Ignoring everything
        // else keeps a busy AppData folder from triggering constant rescans.
        string extension = Path.GetExtension(e.Name ?? string.Empty);

        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(extension))
        {
            Schedule();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Usually a buffer overflow, meaning events were dropped. A rescan is exactly the
        // right response: it re-reads everything anyway.
        _logger.LogWarning(e.GetException(), "A Start Menu watcher reported an error; scheduling a rescan.");
        Schedule();
    }

    private void OnPackageInstalling(PackageCatalog sender, PackageInstallingEventArgs args)
    {
        if (args.IsComplete)
        {
            Schedule();
        }
    }

    private void OnPackageUninstalling(PackageCatalog sender, PackageUninstallingEventArgs args)
    {
        if (args.IsComplete)
        {
            Schedule();
        }
    }

    private void OnPackageUpdating(PackageCatalog sender, PackageUpdatingEventArgs args)
    {
        if (args.IsComplete)
        {
            Schedule();
        }
    }

    /// <summary>Restarts the settle timer, so a burst of changes produces one report.</summary>
    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _settleTimer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _settleTimer.Change(SettleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("App changes settled; requesting a rescan.");
        ChangeDetected?.Invoke(this, EventArgs.Empty);
    }

    public void StopWatching()
    {
        lock (_gate)
        {
            foreach (FileSystemWatcher watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception)
                {
                    // Nothing useful to do while tearing down.
                }
            }

            _watchers.Clear();

            if (_catalog is not null)
            {
                try
                {
                    _catalog.PackageInstalling -= OnPackageInstalling;
                    _catalog.PackageUninstalling -= OnPackageUninstalling;
                    _catalog.PackageUpdating -= OnPackageUpdating;
                }
                catch (Exception)
                {
                    // Ditto.
                }

                _catalog = null;
            }

            _settleTimer?.Dispose();
            _settleTimer = null;

            IsWatching = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatching();
    }
}
