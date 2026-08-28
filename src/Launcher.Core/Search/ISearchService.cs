using Launcher.Core.Models;

namespace Launcher.Core.Search;

/// <summary>One app that matched the query.</summary>
/// <param name="Entry">The matched app.</param>
/// <param name="Score">Final rank, combining match quality with how often and how recently the app is used.</param>
/// <param name="NameMatch">
/// The match against the display name, or null when only a secondary field (the target's
/// file name) matched. Drives the highlighting in the results list.
/// </param>
public sealed record SearchResult(AppEntry Entry, double Score, FuzzyMatch? NameMatch);

/// <summary>Ranks apps against a typed query.</summary>
public interface ISearchService
{
    /// <summary>
    /// Returns the best matches, highest score first. An empty or whitespace query returns
    /// nothing - "no query" means search is inactive, not "everything matches".
    /// </summary>
    /// <param name="query">What the user typed.</param>
    /// <param name="candidates">Apps to search. Callers filter for scope, hidden and filtered entries.</param>
    /// <param name="maxResults">Upper bound on the returned list.</param>
    /// <param name="now">Reference time for recency weighting. Defaults to now; injectable for tests.</param>
    IReadOnlyList<SearchResult> Search(
        string? query,
        IEnumerable<AppEntry> candidates,
        int maxResults = 40,
        DateTimeOffset? now = null);
}
