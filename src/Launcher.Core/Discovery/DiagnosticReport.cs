using System.Globalization;
using System.Text;
using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>
/// Builds the text behind "Copy diagnostics" in Settings.
/// <para>
/// A duplicated tile means two entries whose merge keys differ, and which fields differ is
/// the whole answer. Asking someone to run a script to find that out mostly means never
/// finding out, so the app writes the report itself.
/// </para>
/// <para>
/// Deliberately narrow: what is on the machine and how each app was found. No file
/// contents, no user names beyond the paths that are already app locations, nothing that
/// is not visible in the launcher itself.
/// </para>
/// </summary>
public static class DiagnosticReport
{
    public static string Build(IReadOnlyList<AppEntry> entries, Version version, string installKind)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var report = new StringBuilder();

        report.AppendLine(CultureInfo.InvariantCulture, $"YBO Launcher {version} ({installKind})");
        report.AppendLine(CultureInfo.InvariantCulture, $"Windows {Environment.OSVersion.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"entries: {entries.Count}");

        AppendCounts(report, entries);
        AppendDuplicates(report, entries);
        AppendGames(report, entries);

        return report.ToString();
    }

    private static void AppendCounts(StringBuilder report, IReadOnlyList<AppEntry> entries)
    {
        report.AppendLine();
        report.AppendLine("by source:");

        foreach (IGrouping<AppSource, AppEntry> group in entries.GroupBy(e => e.Source).OrderBy(g => g.Key))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()}");
        }
    }

    private static void AppendDuplicates(StringBuilder report, IReadOnlyList<AppEntry> entries)
    {
        List<IGrouping<string, AppEntry>> duplicates =
        [
            .. entries
                .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName))
                .GroupBy(e => Normalize(e.DisplayName), StringComparer.Ordinal)
                .Where(g => g.Count() > 1),
        ];

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"duplicated names: {duplicates.Count}");

        foreach (IGrouping<string, AppEntry> group in duplicates)
        {
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"=== {group.First().DisplayName} ===");

            foreach (AppEntry entry in group)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"  source={entry.Source} kind={entry.LaunchKind} isGame={entry.IsGame}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    key      = {entry.MergeKey}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    target   = {entry.TargetPath}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    args     = {entry.Arguments}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    uri      = {entry.LaunchUri}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    aumid    = {entry.AppUserModelId}");
                report.AppendLine(CultureInfo.InvariantCulture, $"    shortcut = {entry.ShortcutPath}");
            }
        }
    }

    private static void AppendGames(StringBuilder report, IReadOnlyList<AppEntry> entries)
    {
        List<AppEntry> games = [.. entries.Where(e => e.IsGame).OrderBy(e => e.DisplayName, StringComparer.Ordinal)];

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"games: {games.Count}");

        foreach (AppEntry game in games)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {game.DisplayName} — {game.LaunchUri ?? game.TargetPath}");
        }
    }

    private static string Normalize(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
