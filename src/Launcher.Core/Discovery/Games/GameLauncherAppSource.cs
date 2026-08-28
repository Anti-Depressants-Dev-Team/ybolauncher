using System.Runtime.Versioning;
using Launcher.Core.Icons;
using Launcher.Core.Interop;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// Every installed game launcher, as one discovery source.
/// <para>
/// Each launcher is an <see cref="IGameLibrary"/> that reads that launcher's own
/// bookkeeping, so adding another one is a new small class rather than a change here.
/// Xbox Game Pass titles are deliberately absent: they install as MSIX packages and are
/// already found by the packaged app source.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameLauncherAppSource : IAppSource
{
    private readonly IGameLibrary[] _libraries;
    private readonly IIconService _icons;
    private readonly ILogger<GameLauncherAppSource> _logger;

    public GameLauncherAppSource(
        IEnumerable<IGameLibrary> libraries,
        IIconService icons,
        ILogger<GameLauncherAppSource>? logger = null)
    {
        _libraries = libraries?.ToArray() ?? [];
        _icons = icons;
        _logger = logger ?? NullLogger<GameLauncherAppSource>.Instance;
    }

    public AppSource Kind => AppSource.GameLauncher;

    public string DisplayName => "Game launchers";

    public Task<IReadOnlyList<AppEntry>> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Icon extraction goes through apartment-threaded shell COM, same as the Start
        // Menu walk, so the whole pass runs on one STA thread.
        return StaThread.RunAsync<IReadOnlyList<AppEntry>>(
            () => Scan(context, cancellationToken),
            cancellationToken);
    }

    private List<AppEntry> Scan(DiscoveryContext context, CancellationToken cancellationToken)
    {
        var entries = new List<AppEntry>();

        for (int i = 0; i < _libraries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IGameLibrary library = _libraries[i];
            IReadOnlyList<GameEntry> games;

            try
            {
                games = library.Enumerate();
            }
            catch (Exception ex)
            {
                // One launcher's data being unreadable must not cost the others.
                _logger.LogWarning(ex, "The {Library} library could not be read.", library.Name);
                continue;
            }

            foreach (GameEntry game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AppEntry? entry = Convert(game, context.IconPixelSize);

                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            if (games.Count > 0)
            {
                _logger.LogInformation("{Library} contributed {Count} games.", library.Name, games.Count);
            }

            context.Progress?.Report(new DiscoveryProgress(DisplayName, i + 1, _libraries.Length));
        }

        return entries;
    }

    private AppEntry? Convert(GameEntry game, int iconPixelSize)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            bool hasProtocol = !string.IsNullOrWhiteSpace(game.LaunchUri);

            // The target path is kept even for protocol launches: it is what makes
            // "open file location" work and where the icon comes from.
            string? target = game.ExecutablePath ?? game.InstallDirectory;

            if (!hasProtocol && string.IsNullOrWhiteSpace(target))
            {
                // Nothing to launch and no protocol - not a usable entry.
                return null;
            }

            var entry = new AppEntry
            {
                DisplayName = game.Name,
                OriginalName = game.Name,
                Source = AppSource.GameLauncher,
                LaunchKind = hasProtocol ? LaunchKind.Uri : LaunchKind.Executable,
                LaunchUri = game.LaunchUri,
                TargetPath = target,
                WorkingDirectory = game.InstallDirectory,
            };

            // A Steam shortcut in the Start Menu is a .url holding the same
            // steam://rungameid URI, so this key lets the two merge into one tile
            // instead of the game appearing twice.
            entry.MergeKey = AppIdentity.ForEntry(entry);
            entry.Id = AppIdentity.ToId(entry.MergeKey);
            entry.IconCacheFile = ExtractIcon(game, iconPixelSize);

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping the game {Name}.", game.Name);
            return null;
        }
    }

    /// <summary>
    /// Prefers an icon the launcher already cached, then the game's executable, then the
    /// install folder. Steam is the only launcher that reliably caches one.
    /// </summary>
    private string? ExtractIcon(GameEntry game, int iconPixelSize)
    {
        foreach (string? candidate in new[] { game.IconPath, game.ExecutablePath, game.InstallDirectory })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (_icons.ExtractFromPath(candidate, iconPixelSize) is { } cached)
            {
                return cached;
            }
        }

        return null;
    }
}
