namespace Launcher.Core.Search;

/// <summary>A successful fuzzy match.</summary>
/// <param name="Score">
/// Higher is better. Only comparable between matches of the <em>same</em> pattern.
/// </param>
/// <param name="Positions">
/// Indices in the searched text that the pattern matched, ascending. Used to highlight
/// the matched characters in the results list.
/// </param>
public sealed record FuzzyMatch(int Score, IReadOnlyList<int> Positions)
{
    /// <summary>Index of the first matched character. Earlier matches rank higher.</summary>
    public int FirstPosition => Positions.Count > 0 ? Positions[0] : int.MaxValue;

    /// <summary>
    /// Splits <paramref name="text"/> into alternating plain and matched runs, ready for
    /// rendering as highlighted inlines.
    /// </summary>
    public IReadOnlyList<TextSegment> ToSegments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var segments = new List<TextSegment>();
        var matched = new HashSet<int>(Positions);

        int index = 0;
        while (index < text.Length)
        {
            bool isMatch = matched.Contains(index);
            int start = index;

            while (index < text.Length && matched.Contains(index) == isMatch)
            {
                index++;
            }

            segments.Add(new TextSegment(text[start..index], isMatch));
        }

        return segments;
    }
}

/// <summary>A run of text that is either part of the match or not.</summary>
public sealed record TextSegment(string Text, bool IsMatch);
