using Launcher.Core.Search;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class FuzzyMatcherTests
{
    private static int ScoreOf(string pattern, string text)
    {
        FuzzyMatch? match = FuzzyMatcher.Match(pattern, text);
        Assert.NotNull(match);
        return match.Score;
    }

    [Theory]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("VSC", "visual studio code")]
    [InlineData("vscode", "Visual Studio Code")]
    [InlineData("f", "Firefox")]
    public void Matches_whenThePatternIsASubsequence(string pattern, string text)
    {
        Assert.NotNull(FuzzyMatcher.Match(pattern, text));
    }

    [Theory]
    [InlineData("xyz", "Visual Studio Code")]
    [InlineData("cv", "Visual Studio Code")]
    [InlineData("codee", "Visual Studio Code")]
    public void DoesNotMatch_whenThePatternIsNotASubsequence(string pattern, string text)
    {
        // "cv" fails because order matters: the characters must appear in sequence.
        Assert.Null(FuzzyMatcher.Match(pattern, text));
    }

    [Theory]
    [InlineData(null, "Steam")]
    [InlineData("", "Steam")]
    [InlineData("steam", null)]
    [InlineData("steam", "")]
    [InlineData("longer than the text", "Steam")]
    public void ReturnsNull_forDegenerateInput(string? pattern, string? text)
    {
        Assert.Null(FuzzyMatcher.Match(pattern, text));
    }

    [Fact]
    public void Positions_areAscendingAndPointAtTheMatchedCharacters()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("code", "Visual Studio Code");

        Assert.NotNull(match);
        Assert.Equal(4, match.Positions.Count);
        Assert.Equal([14, 15, 16, 17], match.Positions);
        Assert.Equal("Code", new string([.. match.Positions.Select(p => "Visual Studio Code"[p])]));
    }

    [Fact]
    public void ChoosesTheBestAlignment_notTheFirstOneThatFits()
    {
        // A greedy left-to-right match takes the "s" of "Visual" at index 2. The optimal
        // alignment is "St" at the start of "Studio", which is a word boundary followed by
        // a consecutive character.
        FuzzyMatch? match = FuzzyMatcher.Match("st", "Visual Studio");

        Assert.NotNull(match);
        Assert.Equal([7, 8], match.Positions);
    }

    [Fact]
    public void PrefixMatch_scoresHigherThanTheSameCharactersMidWord()
    {
        Assert.True(ScoreOf("ste", "Steam") > ScoreOf("ste", "Systemsteuerung"));
    }

    [Fact]
    public void ConsecutiveCharacters_scoreHigherThanScatteredOnes()
    {
        Assert.True(ScoreOf("abc", "abcdefgh") > ScoreOf("abc", "axbxcxdx"));
    }

    [Fact]
    public void WordBoundaries_scoreHigherThanMidWordCharacters()
    {
        // Same characters, same gaps - the only difference is that one lands on the start
        // of each word.
        Assert.True(ScoreOf("ab", "Alpha Bravo") > ScoreOf("ab", "Xalpha xbravo"));
    }

    [Fact]
    public void CamelCaseHumps_countAsBoundaries()
    {
        Assert.True(ScoreOf("cc", "myCamelCase") > ScoreOf("cc", "mycamelcase"));
    }

    [Fact]
    public void BoundaryAfterAGap_beatsAMidWordCharacterAfterTheSameGap()
    {
        // Identical gap length; the only difference is that "B" starts a word. Skipping a
        // whole word to reach an initial is a different intent from skipping letters
        // inside one, and plain fzf charges both the same. See BoundaryGapRefund.
        Assert.True(ScoreOf("ab", "Alpha Bravo") > ScoreOf("ab", "Alphaxbravo"));
    }

    [Fact]
    public void AcronymMatching_picksTheWordInitials()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("vsc", "Visual Studio Code");

        Assert.NotNull(match);
        Assert.Equal([0, 7, 14], match.Positions);
    }

    [Fact]
    public void ShorterGaps_scoreHigherThanLongerOnes()
    {
        Assert.True(ScoreOf("ab", "axb") > ScoreOf("ab", "axxxxxxb"));
    }

    [Fact]
    public void MatchIsCaseInsensitiveButPositionsIndexTheOriginalText()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("FIREFOX", "Firefox");

        Assert.NotNull(match);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], match.Positions);
    }

    [Fact]
    public void VeryLongText_isTruncatedRatherThanRefused()
    {
        string text = new string('a', FuzzyMatcher.MaxTextLength) + "zzz";

        // "z" only exists past the cap, so it must not match.
        Assert.Null(FuzzyMatcher.Match("z", text));
        Assert.NotNull(FuzzyMatcher.Match("aa", text));
    }

    [Fact]
    public void Segments_splitTheTextIntoMatchedAndUnmatchedRuns()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("vsc", "Visual Studio Code");
        Assert.NotNull(match);

        IReadOnlyList<TextSegment> segments = match.ToSegments("Visual Studio Code");

        // Reassembling the segments must reproduce the original text exactly, or the
        // highlighted label would not match the app's name.
        Assert.Equal("Visual Studio Code", string.Concat(segments.Select(s => s.Text)));
        Assert.Equal("VSC", string.Concat(segments.Where(s => s.IsMatch).Select(s => s.Text)));
    }

    [Fact]
    public void Segments_ofAFullyMatchedString_areASingleMatchedRun()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("abc", "abc");
        Assert.NotNull(match);

        IReadOnlyList<TextSegment> segments = match.ToSegments("abc");

        Assert.Single(segments);
        Assert.True(segments[0].IsMatch);
    }
}
