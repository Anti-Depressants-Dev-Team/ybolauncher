using Launcher.Core.Models;

namespace Launcher.Core.Search;

/// <inheritdoc cref="ISearchService"/>
public sealed class SearchService : ISearchService
{
    /// <summary>
    /// Penalty per character of the first match's offset. A match at the very start of the
    /// name beats the same match buried in the middle of a longer one.
    /// </summary>
    public const double PositionWeight = 1.0;

    /// <summary>Mild preference for shorter names, used mostly to break ties.</summary>
    public const double LengthWeight = 0.05;

    /// <summary>Scale of the launch-count boost. Logarithmic, so heavy use cannot drown out match quality.</summary>
    public const double FrequencyWeight = 4.0;

    /// <summary>Scale of the "used recently" boost.</summary>
    public const double RecencyWeight = 10.0;

    /// <summary>
    /// Ceiling on the combined frequency and recency boost.
    /// <para>
    /// Usage is a tie-breaker between plausible matches, not a substitute for matching.
    /// Without a cap, an app launched hundreds of times outranks one whose full name the
    /// user just typed exactly - which is never what they meant.
    /// </para>
    /// </summary>
    public const double MaxUsageBoost = 25.0;

    /// <summary>
    /// Awarded when the query is the whole display name. Typing an app's exact name is an
    /// unambiguous statement of intent and must not lose to a heavily used app that merely
    /// contains the same letters.
    /// </summary>
    public const double ExactNameBonus = 40.0;

    /// <summary>Awarded when the query is a prefix of the display name.</summary>
    public const double PrefixBonus = 15.0;

    /// <summary>Days over which the recency boost decays by roughly a factor of e.</summary>
    public const double RecencyDecayDays = 14.0;

    /// <summary>
    /// Charged when only the target's file name matched. Typing "devenv" should still find
    /// Visual Studio, but never above an app whose visible name actually matches.
    /// </summary>
    public const double SecondaryFieldPenalty = 20.0;

    public IReadOnlyList<SearchResult> Search(
        string? query,
        IEnumerable<AppEntry> candidates,
        int maxResults = 40,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        string pattern = query?.Trim() ?? string.Empty;

        if (pattern.Length == 0 || maxResults <= 0)
        {
            return [];
        }

        DateTimeOffset reference = now ?? DateTimeOffset.UtcNow;
        var results = new List<SearchResult>();

        foreach (AppEntry entry in candidates)
        {
            SearchResult? result = Rank(pattern, entry, reference);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        // Name is the tie-break so repeated searches produce a stable order rather than
        // shuffling equally-scored apps around under the cursor.
        results.Sort(static (left, right) =>
        {
            int byScore = right.Score.CompareTo(left.Score);
            return byScore != 0
                ? byScore
                : StringComparer.CurrentCultureIgnoreCase.Compare(left.Entry.DisplayName, right.Entry.DisplayName);
        });

        return results.Count > maxResults ? results[..maxResults] : results;
    }

    private static SearchResult? Rank(string pattern, AppEntry entry, DateTimeOffset now)
    {
        FuzzyMatch? nameMatch = FuzzyMatcher.Match(pattern, entry.DisplayName);

        double best = double.NegativeInfinity;

        if (nameMatch is not null)
        {
            best = nameMatch.Score
                - (PositionWeight * nameMatch.FirstPosition)
                - (LengthWeight * entry.DisplayName.Length);

            // Exact and prefix matches are qualitatively different from a good fuzzy
            // match, and the raw score does not separate them by enough on its own.
            if (string.Equals(entry.DisplayName, pattern, StringComparison.OrdinalIgnoreCase))
            {
                best += ExactNameBonus;
            }
            else if (entry.DisplayName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            {
                best += PrefixBonus;
            }
        }

        // A secondary pass over the executable's file name, so "devenv" or "msedge" find
        // the app even though neither appears in its display name.
        string? fileName = TryGetFileName(entry.TargetPath);

        if (fileName is not null
            && !string.Equals(fileName, entry.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            FuzzyMatch? fileMatch = FuzzyMatcher.Match(pattern, fileName);

            if (fileMatch is not null)
            {
                double fileScore = fileMatch.Score
                    - (PositionWeight * fileMatch.FirstPosition)
                    - (LengthWeight * fileName.Length)
                    - SecondaryFieldPenalty;

                best = Math.Max(best, fileScore);
            }
        }

        if (double.IsNegativeInfinity(best))
        {
            return null;
        }

        return new SearchResult(entry, best + UsageBoost(entry, now), nameMatch);
    }

    /// <summary>
    /// How much an app's own usage lifts it. Frequency is logarithmic and recency decays
    /// exponentially, so a familiar app surfaces early without a long-unused one being
    /// permanently buried by something launched once.
    /// </summary>
    private static double UsageBoost(AppEntry entry, DateTimeOffset now)
    {
        double frequency = FrequencyWeight * Math.Log2(1 + Math.Max(0, entry.LaunchCount));

        if (entry.LastLaunchedUtc is not { } lastLaunched)
        {
            return Math.Min(frequency, MaxUsageBoost);
        }

        double days = (now - lastLaunched).TotalDays;

        if (days < 0)
        {
            // A clock change should not hand out an unbounded bonus.
            days = 0;
        }

        double recency = RecencyWeight * Math.Exp(-days / RecencyDecayDays);

        return Math.Min(frequency + recency, MaxUsageBoost);
    }

    private static string? TryGetFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
