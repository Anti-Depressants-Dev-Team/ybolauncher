using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>
/// Collapses the raw output of every discovery source into one entry per app.
/// <para>
/// Grouping is on <see cref="AppEntry.MergeKey"/>. The common cases this fixes are a
/// shortcut present in both the machine and user Start Menu folders, several shortcuts
/// pointing at the same executable, and a Store app that appears both in the package
/// catalog and as a Start Menu shortcut carrying its AUMID.
/// </para>
/// </summary>
public static class AppDeduplicator
{
    /// <summary>
    /// Merges duplicates. Output order is the first-seen order of each distinct app, so
    /// repeated scans produce a stable list.
    /// </summary>
    public static List<AppEntry> Merge(IEnumerable<AppEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var groups = new Dictionary<string, List<AppEntry>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (AppEntry entry in entries)
        {
            string key = string.IsNullOrEmpty(entry.MergeKey)
                ? AppIdentity.ForEntry(entry)
                : entry.MergeKey;

            if (!groups.TryGetValue(key, out List<AppEntry>? group))
            {
                group = [];
                groups[key] = group;
                order.Add(key);
            }

            group.Add(entry);
        }

        var merged = new List<AppEntry>(order.Count);

        foreach (string key in order)
        {
            merged.Add(MergeGroup(key, groups[key]));
        }

        return CollapseSameInstall(CollapseGameShortcuts(merged));
    }

    /// <summary>
    /// Collapses one application that reached us by two different paths inside its own
    /// install folder.
    /// <para>
    /// Electron and Squirrel apps keep a versioned folder per release beside a stub, so a
    /// single app owns <c>…\Medal\current\Medal.exe</c> and <c>…\Medal\app-4.1.2\Medal.exe</c>
    /// at the same time. Shortcuts to each are different targets, so the merge key cannot
    /// tell they are one app, and it appears twice.
    /// </para>
    /// <para>
    /// The evidence is the same name plus a shared install folder - see
    /// <see cref="InstallTree"/>, which is what stops two unrelated apps under Program
    /// Files looking like one. Packaged apps are left out: an app installed both from the
    /// Store and as a desktop program really is two installs.
    /// </para>
    /// </summary>
    private static List<AppEntry> CollapseSameInstall(List<AppEntry> merged)
    {
        var absorbed = new HashSet<AppEntry>();

        foreach (IGrouping<string, AppEntry> group in merged
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName)
                && !string.IsNullOrWhiteSpace(e.TargetPath)
                && string.IsNullOrWhiteSpace(e.AppUserModelId))
            .GroupBy(e => NormalizeName(e.DisplayName), StringComparer.Ordinal)
            .Where(g => g.Count() > 1))
        {
            // The shallowest target is the install root - for a Squirrel app the stub that
            // survives its next update, rather than a folder named after this version.
            List<AppEntry> candidates = [.. group.OrderBy(e => Depth(e.TargetPath))];
            AppEntry primary = candidates[0];

            foreach (AppEntry other in candidates.Skip(1))
            {
                if (!InstallTree.ShareAnInstallFolder(primary.TargetPath, other.TargetPath))
                {
                    continue;
                }

                primary.ShortcutPath ??= other.ShortcutPath;
                primary.IconCacheFile ??= other.IconCacheFile;
                primary.WorkingDirectory ??= other.WorkingDirectory;
                primary.IsGame |= other.IsGame;

                // One good shortcut redeems a group, as in the key-based merge.
                if (!other.IsFiltered && primary.IsFiltered)
                {
                    primary.IsFiltered = false;
                    primary.FilterReason = FilterReason.None;
                }

                absorbed.Add(other);
            }
        }

        return absorbed.Count == 0 ? merged : [.. merged.Where(e => !absorbed.Contains(e))];
    }

    private static int Depth(string? path) =>
        path?.Count(c => c is '\\' or '/') ?? int.MaxValue;

    /// <summary>
    /// Collapses a game found in a launcher's library with that launcher's own Start Menu
    /// shortcut for it.
    /// <para>
    /// A store whose games launch through a protocol needs nothing here: the shortcut is a
    /// <c>.url</c> holding the same URI, so both sides already produce the same merge key.
    /// The stores whose games run directly - HoYoPlay and Rockstar especially - write a
    /// shortcut that goes through the launcher's own executable instead, which is a
    /// different target and so a different key, and the game would appear twice.
    /// </para>
    /// <para>
    /// Matching is on the display name, which for a game is distinctive enough to be
    /// trustworthy. It is deliberately narrow: exactly one library entry and only Start
    /// Menu shortcuts beside it, so two genuinely separate installs of the same title are
    /// left alone.
    /// </para>
    /// </summary>
    private static List<AppEntry> CollapseGameShortcuts(List<AppEntry> merged)
    {
        var byName = merged
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName))
            .GroupBy(e => NormalizeName(e.DisplayName), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        if (byName.Count == 0)
        {
            return merged;
        }

        var absorbed = new HashSet<AppEntry>();

        foreach (IGrouping<string, AppEntry> group in byName)
        {
            List<AppEntry> games = [.. group.Where(e => e.IsGame)];
            List<AppEntry> shortcuts = [.. group.Where(e => !e.IsGame && e.Source == AppSource.StartMenu)];

            if (games.Count != 1 || shortcuts.Count == 0 || games.Count + shortcuts.Count != group.Count())
            {
                continue;
            }

            AppEntry game = games[0];

            // The game keeps its identity, so the Games tab holds the same id whether or
            // not a shortcut happens to exist.
            foreach (AppEntry shortcut in shortcuts)
            {
                // The launcher's own shortcut knows how the launcher wants the game
                // started, so its route wins - path and arguments together, never one
                // from each, which would produce a command that runs the wrong thing.
                if (game.LaunchUri is null && shortcut.TargetPath is not null)
                {
                    game.TargetPath = shortcut.TargetPath;
                    game.Arguments = shortcut.Arguments;
                    game.LaunchKind = shortcut.LaunchKind;
                    game.WorkingDirectory = shortcut.WorkingDirectory ?? game.WorkingDirectory;
                }

                game.ShortcutPath ??= shortcut.ShortcutPath;

                // The library's icon is the game's own; a shortcut's is often the
                // launcher's.
                game.IconCacheFile ??= shortcut.IconCacheFile;

                absorbed.Add(shortcut);
            }
        }

        return absorbed.Count == 0 ? merged : [.. merged.Where(e => !absorbed.Contains(e))];
    }

    /// <summary>Case- and spacing-insensitive form of a display name.</summary>
    private static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static AppEntry MergeGroup(string key, List<AppEntry> group)
    {
        if (group.Count == 1)
        {
            AppEntry only = group[0];
            only.MergeKey = key;
            only.Id = AppIdentity.ToId(key);
            return only;
        }

        // The package catalog entry, when present, owns how the app is launched:
        // LaunchAsync through the catalog is more reliable than any path a shortcut
        // carries. Selection is on Source, not LaunchKind - a Start Menu shortcut for a
        // Store app also has LaunchKind.PackagedApp, and picking it would make the merged
        // result depend on enumeration order.
        AppEntry primary = group.FirstOrDefault(e => e.Source == AppSource.Packaged)
            ?? group.FirstOrDefault(e => e.LaunchKind == LaunchKind.PackagedApp)
            ?? group[0];

        var result = new AppEntry
        {
            MergeKey = key,
            Id = AppIdentity.ToId(key),
            Source = primary.Source,
            LaunchKind = primary.LaunchKind,
            TargetPath = primary.TargetPath,
            LaunchUri = primary.LaunchUri,
            Arguments = primary.Arguments,
            WorkingDirectory = primary.WorkingDirectory,
            AppUserModelId = primary.AppUserModelId,
            PackageFamilyName = primary.PackageFamilyName,
        };

        // Fill any gap in the primary from the other duplicates rather than losing data.
        foreach (AppEntry candidate in group)
        {
            result.TargetPath ??= candidate.TargetPath;
            result.LaunchUri ??= candidate.LaunchUri;
            result.Arguments ??= candidate.Arguments;
            result.WorkingDirectory ??= candidate.WorkingDirectory;
            result.AppUserModelId ??= candidate.AppUserModelId;
            result.PackageFamilyName ??= candidate.PackageFamilyName;
            result.ShortcutPath ??= candidate.ShortcutPath;
            result.IconCacheFile ??= candidate.IconCacheFile;
        }

        // A game that also has a Start Menu shortcut stays a game whichever route won,
        // or it would fall out of the Games tab.
        result.IsGame = group.Any(e => e.IsGame);

        result.OriginalName = ChooseName(group);
        result.DisplayName = result.OriginalName;

        // One good shortcut redeems a group: an app is only clutter when every way of
        // reaching it is clutter.
        AppEntry? kept = group.FirstOrDefault(e => !e.IsFiltered);
        result.IsFiltered = kept is null;
        result.FilterReason = kept is null ? group[0].FilterReason : FilterReason.None;

        return result;
    }

    /// <summary>
    /// Picks the display name for a merged group: the packaged catalog's name if there is
    /// one, otherwise the most frequently seen name, tie-broken by shortest then ordinal
    /// so the result does not depend on enumeration order.
    /// </summary>
    private static string ChooseName(List<AppEntry> group)
    {
        AppEntry? packaged = group.FirstOrDefault(
            e => e.LaunchKind == LaunchKind.PackagedApp && !string.IsNullOrWhiteSpace(e.OriginalName));

        if (packaged is not null)
        {
            return packaged.OriginalName;
        }

        return group
            .Select(e => e.OriginalName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Length)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;
    }
}
