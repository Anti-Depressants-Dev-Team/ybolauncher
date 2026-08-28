namespace Launcher.Core.Search;

/// <summary>
/// fzf-style fuzzy matching: the pattern must appear as a subsequence of the text, and the
/// score rewards matches that land on word boundaries and run consecutively.
/// <para>
/// Alignment is chosen by dynamic programming rather than greedily, because the first
/// place a character fits is often not the best one - for "gc", the "c" of "Chrome" in
/// "Google Chrome" scores far better than the "c" in "Google".
/// </para>
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Awarded for every matched character.</summary>
    public const int ScoreMatch = 16;

    /// <summary>Matching the first character of a word.</summary>
    public const int BonusBoundary = ScoreMatch / 2;

    /// <summary>Matching punctuation, which is nearly always deliberate.</summary>
    public const int BonusNonWord = ScoreMatch / 2;

    /// <summary>Matching a camelCase hump or the first digit of a number.</summary>
    public const int BonusCamel = BonusBoundary - 1;

    /// <summary>Matching immediately after the previous matched character.</summary>
    public const int BonusConsecutive = 4;

    /// <summary>Cost of opening a gap between two matched characters.</summary>
    public const int GapStart = -3;

    /// <summary>Cost of each additional skipped character.</summary>
    public const int GapExtension = -1;

    /// <summary>The first matched character's boundary bonus counts double.</summary>
    public const int FirstCharMultiplier = 2;

    /// <summary>
    /// Partial refund of the gap cost when the character after the gap starts a word.
    /// <para>
    /// This is a deliberate deviation from fzf. Skipping whole words to match an acronym
    /// is a different intent from skipping letters inside one word, and plain fzf scoring
    /// charges both the same. Without it "vs" ranks "Advanced Vs Settings" above "Visual
    /// Studio Code", which SPEC.md calls out as the wrong answer.
    /// </para>
    /// </summary>
    public const int BoundaryGapRefund = 6;

    /// <summary>
    /// Text longer than this is truncated before matching. App names are far shorter, and
    /// the DP is O(pattern x text).
    /// </summary>
    public const int MaxTextLength = 256;

    /// <summary>Sentinel for "no valid alignment ends here".</summary>
    private const int Invalid = int.MinValue / 2;

    private enum CharClass
    {
        White,
        NonWord,
        Delimiter,
        Lower,
        Upper,
        Digit,
    }

    /// <summary>
    /// Matches <paramref name="pattern"/> against <paramref name="text"/>, returning null
    /// when the pattern is not a subsequence of the text.
    /// </summary>
    public static FuzzyMatch? Match(string? pattern, string? text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
        {
            return null;
        }

        string haystack = text.Length > MaxTextLength ? text[..MaxTextLength] : text;

        int m = pattern.Length;
        int n = haystack.Length;

        if (m > n)
        {
            return null;
        }

        // Cheap rejection first: most candidates fail here, and this avoids allocating the
        // DP tables for them.
        if (!IsSubsequence(pattern, haystack))
        {
            return null;
        }

        int[] bonus = ComputeBonuses(haystack);
        int[] scores = new int[m * n];
        int[] parents = new int[m * n];

        for (int i = 0; i < m; i++)
        {
            // Best score for pattern[i-1] ending at some k <= j-2, already carrying the
            // gap penalty for reaching j. k = j-1 is the consecutive case, handled below.
            int running = Invalid;
            int runningIndex = -1;

            for (int j = 0; j < n; j++)
            {
                if (i > 0 && j >= 2)
                {
                    if (running > Invalid)
                    {
                        // Every candidate already in the window just grew its gap by one.
                        running += GapExtension;
                    }

                    int arriving = scores[((i - 1) * n) + (j - 2)];
                    if (arriving > Invalid && arriving + GapStart > running)
                    {
                        running = arriving + GapStart;
                        runningIndex = j - 2;
                    }
                }

                int cell = Invalid;
                int parent = -1;

                if (Fold(haystack[j]) == Fold(pattern[i]))
                {
                    if (i == 0)
                    {
                        cell = ScoreMatch + (bonus[j] * FirstCharMultiplier);
                    }
                    else
                    {
                        if (j >= 1)
                        {
                            int previous = scores[((i - 1) * n) + (j - 1)];
                            if (previous > Invalid)
                            {
                                cell = previous + ScoreMatch + Math.Max(bonus[j], BonusConsecutive);
                                parent = j - 1;
                            }
                        }

                        if (running > Invalid)
                        {
                            int gapped = running + ScoreMatch + bonus[j];

                            if (bonus[j] >= BonusBoundary)
                            {
                                gapped += BoundaryGapRefund;
                            }

                            if (gapped > cell)
                            {
                                cell = gapped;
                                parent = runningIndex;
                            }
                        }
                    }
                }

                scores[(i * n) + j] = cell;
                parents[(i * n) + j] = parent;
            }
        }

        int bestScore = Invalid;
        int bestIndex = -1;

        for (int j = 0; j < n; j++)
        {
            int candidate = scores[((m - 1) * n) + j];
            if (candidate > bestScore)
            {
                bestScore = candidate;
                bestIndex = j;
            }
        }

        if (bestIndex < 0 || bestScore <= Invalid)
        {
            return null;
        }

        var positions = new int[m];
        int cursor = bestIndex;

        for (int i = m - 1; i >= 0; i--)
        {
            positions[i] = cursor;
            cursor = parents[(i * n) + cursor];
        }

        return new FuzzyMatch(bestScore, positions);
    }

    private static bool IsSubsequence(string pattern, string text)
    {
        int p = 0;

        for (int i = 0; i < text.Length && p < pattern.Length; i++)
        {
            if (Fold(text[i]) == Fold(pattern[p]))
            {
                p++;
            }
        }

        return p == pattern.Length;
    }

    /// <summary>
    /// Bonus each position would earn if matched, from the class of the character before
    /// it. The start of the string counts as following whitespace, so the very first
    /// character is a word boundary.
    /// </summary>
    private static int[] ComputeBonuses(string text)
    {
        var bonus = new int[text.Length];
        CharClass previous = CharClass.White;

        for (int i = 0; i < text.Length; i++)
        {
            CharClass current = Classify(text[i]);
            bonus[i] = BonusFor(previous, current);
            previous = current;
        }

        return bonus;
    }

    private static int BonusFor(CharClass previous, CharClass current)
    {
        bool currentIsWord = current is CharClass.Lower or CharClass.Upper or CharClass.Digit;

        if (currentIsWord && previous is CharClass.White or CharClass.Delimiter or CharClass.NonWord)
        {
            return BonusBoundary;
        }

        if (previous == CharClass.Lower && current == CharClass.Upper)
        {
            return BonusCamel;
        }

        if (previous != CharClass.Digit && current == CharClass.Digit)
        {
            return BonusCamel;
        }

        if (current == CharClass.NonWord)
        {
            return BonusNonWord;
        }

        return 0;
    }

    private static CharClass Classify(char value)
    {
        if (char.IsWhiteSpace(value))
        {
            return CharClass.White;
        }

        if (char.IsDigit(value))
        {
            return CharClass.Digit;
        }

        if (char.IsUpper(value))
        {
            return CharClass.Upper;
        }

        if (char.IsLower(value))
        {
            return CharClass.Lower;
        }

        return value is '/' or '\\' or '-' or '_' or '.' or ':' or ',' or ';' or '(' or ')' or '[' or ']'
            ? CharClass.Delimiter
            : CharClass.NonWord;
    }

    private static char Fold(char value) => char.ToLowerInvariant(value);
}
