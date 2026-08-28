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

        return merged;
    }

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
