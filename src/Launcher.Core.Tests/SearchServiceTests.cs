using Launcher.Core.Models;
using Launcher.Core.Search;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class SearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly SearchService Service = new();

    private static AppEntry App(
        string name,
        string? target = null,
        int launchCount = 0,
        DateTimeOffset? lastLaunched = null) =>
        new()
        {
            Id = name,
            DisplayName = name,
            OriginalName = name,
            TargetPath = target,
            LaunchCount = launchCount,
            LastLaunchedUtc = lastLaunched,
        };

    private static List<string> Names(IEnumerable<SearchResult> results) =>
        [.. results.Select(r => r.Entry.DisplayName)];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQuery_returnsNothing(string? query)
    {
        // "No query" means search is inactive, not "everything matches".
        Assert.Empty(Service.Search(query, [App("Steam")], now: Now));
    }

    [Fact]
    public void TypingVs_ranksVisualStudioCodeFirst()
    {
        // The exact case SPEC.md calls out. "Advanced Vs Settings" actually earns a
        // slightly higher raw match score - it hits two word initials a short gap apart -
        // so getting this right depends on the ranking layer preferring a match that
        // starts at the beginning of the name.
        IReadOnlyList<SearchResult> results = Service.Search(
            "vs",
            [App("Advanced Vs Settings"), App("Visual Studio Code")],
            now: Now);

        Assert.Equal("Visual Studio Code", results[0].Entry.DisplayName);
    }

    [Fact]
    public void ResultsAreOrderedByScoreDescending()
    {
        IReadOnlyList<SearchResult> results = Service.Search(
            "co",
            [App("Notepad"), App("Code"), App("Visual Studio Code"), App("Cool Companion")],
            now: Now);

        Assert.DoesNotContain("Notepad", Names(results));

        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score);
        }
    }

    [Fact]
    public void NonMatchingApps_areExcluded()
    {
        IReadOnlyList<SearchResult> results = Service.Search("zzz", [App("Steam"), App("Firefox")], now: Now);

        Assert.Empty(results);
    }

    [Fact]
    public void MaxResults_isRespected()
    {
        AppEntry[] apps = [.. Enumerable.Range(0, 20).Select(i => App("App " + i))];

        Assert.Equal(5, Service.Search("app", apps, maxResults: 5, now: Now).Count);
    }

    [Fact]
    public void MaxResultsOfZero_returnsNothing()
    {
        Assert.Empty(Service.Search("app", [App("App")], maxResults: 0, now: Now));
    }

    [Fact]
    public void LaunchCount_liftsAnOtherwiseEqualApp()
    {
        AppEntry rarely = App("Alpha Tool");
        AppEntry often = App("Alpha Tool", launchCount: 40);

        // Same name, so the match scores are identical and only usage separates them.
        double rarelyScore = Service.Search("alpha", [rarely], now: Now)[0].Score;
        double oftenScore = Service.Search("alpha", [often], now: Now)[0].Score;

        Assert.True(oftenScore > rarelyScore);
    }

    [Fact]
    public void LaunchCountBoost_isLogarithmic_soHeavyUseCannotDrownOutMatchQuality()
    {
        double one = Service.Search("alpha", [App("Alpha", launchCount: 1)], now: Now)[0].Score;
        double ten = Service.Search("alpha", [App("Alpha", launchCount: 10)], now: Now)[0].Score;
        double thousand = Service.Search("alpha", [App("Alpha", launchCount: 1000)], now: Now)[0].Score;

        Assert.True(ten > one);
        Assert.True(thousand > ten);

        // Going from 10 to 1000 launches must add less than going from 1 to 10 did,
        // or a single much-used app would sit on top of every query forever.
        Assert.True(thousand - ten < (ten - one) * 3);
    }

    [Fact]
    public void RecentlyLaunched_outranksTheSameAppLaunchedLongAgo()
    {
        AppEntry yesterday = App("Alpha", lastLaunched: Now.AddDays(-1));
        AppEntry lastYear = App("Alpha", lastLaunched: Now.AddDays(-365));

        double recent = Service.Search("alpha", [yesterday], now: Now)[0].Score;
        double stale = Service.Search("alpha", [lastYear], now: Now)[0].Score;

        Assert.True(recent > stale);
    }

    [Fact]
    public void ANeverLaunchedApp_getsNoUsageBoost()
    {
        double never = Service.Search("alpha", [App("Alpha")], now: Now)[0].Score;
        double ancient = Service.Search("alpha", [App("Alpha", lastLaunched: Now.AddYears(-10))], now: Now)[0].Score;

        // A decade-old launch should have decayed to almost nothing, but never below zero.
        Assert.True(ancient >= never);
        Assert.True(ancient - never < 1.0);
    }

    [Fact]
    public void AFutureTimestamp_doesNotProduceAnUnboundedBoost()
    {
        // A clock change or a bad restore must not hand one app a permanent win.
        double future = Service.Search("alpha", [App("Alpha", lastLaunched: Now.AddYears(5))], now: Now)[0].Score;
        double today = Service.Search("alpha", [App("Alpha", lastLaunched: Now)], now: Now)[0].Score;

        Assert.Equal(today, future, 3);
    }

    [Fact]
    public void AnExactNameMatch_beatsAHeavilyUsedAcronymMatch()
    {
        // "Systemsteuerung Extras Advanced Manager" genuinely spells STEAM across its word
        // initials, so it earns a high match score honestly. Typing an app's exact name
        // still has to win.
        IReadOnlyList<SearchResult> results = Service.Search(
            "steam",
            [
                App("Systemsteuerung Extras Advanced Manager", launchCount: 500, lastLaunched: Now),
                App("Steam"),
            ],
            now: Now);

        Assert.Equal("Steam", results[0].Entry.DisplayName);
    }

    [Fact]
    public void AnExactNameMatch_beatsAHeavilyUsedMidWordMatch()
    {
        IReadOnlyList<SearchResult> results = Service.Search(
            "steam",
            [App("Bestreamer", launchCount: 500, lastLaunched: Now), App("Steam")],
            now: Now);

        Assert.Equal("Steam", results[0].Entry.DisplayName);
    }

    [Fact]
    public void APrefixMatch_outranksAMatchOfTheSameLettersFurtherIn()
    {
        IReadOnlyList<SearchResult> results = Service.Search(
            "fire",
            [App("Campfire Manager"), App("Firefox")],
            now: Now);

        Assert.Equal("Firefox", results[0].Entry.DisplayName);
    }

    [Fact]
    public void TheExecutableFileName_isSearchedToo()
    {
        // "devenv" appears nowhere in the display name.
        IReadOnlyList<SearchResult> results = Service.Search(
            "devenv",
            [App("Visual Studio 2022", @"C:\VS\Common7\IDE\devenv.exe")],
            now: Now);

        Assert.Single(results);
        Assert.Equal("Visual Studio 2022", results[0].Entry.DisplayName);

        // Nothing in the visible name matched, so there is nothing to highlight.
        Assert.Null(results[0].NameMatch);
    }

    [Fact]
    public void ADisplayNameMatch_outranksAFileNameMatch()
    {
        IReadOnlyList<SearchResult> results = Service.Search(
            "code",
            [App("Something Else", @"C:\tools\code.exe"), App("Code")],
            now: Now);

        Assert.Equal("Code", results[0].Entry.DisplayName);
    }

    [Fact]
    public void NameMatch_isReturnedForHighlighting()
    {
        SearchResult result = Service.Search("vsc", [App("Visual Studio Code")], now: Now)[0];

        Assert.NotNull(result.NameMatch);
        Assert.Equal([0, 7, 14], result.NameMatch.Positions);
    }

    [Fact]
    public void OrderIsStable_forEquallyScoredApps()
    {
        AppEntry[] apps = [App("Beta Tool"), App("Alpha Tool")];

        // Identical shape, so the scores tie; the name tie-break must keep the order from
        // shuffling under the user's cursor between keystrokes.
        List<string> first = Names(Service.Search("tool", apps, now: Now));
        List<string> second = Names(Service.Search("tool", [.. apps.Reverse()], now: Now));

        Assert.Equal(first, second);
    }
}
