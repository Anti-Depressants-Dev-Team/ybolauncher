using Launcher.Core.Discovery.Games;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class VdfParserTests
{
    [Fact]
    public void ParsesNestedBlocksAndValues()
    {
        VdfNode? root = VdfParser.Parse("""
            "AppState"
            {
                "appid"     "440"
                "name"      "Team Fortress 2"
                "UserConfig"
                {
                    "language"  "english"
                }
            }
            """);

        Assert.NotNull(root);
        Assert.Equal("440", root.GetString("AppState", "appid"));
        Assert.Equal("Team Fortress 2", root.GetString("AppState", "name"));
        Assert.Equal("english", root.GetString("AppState", "UserConfig", "language"));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        VdfNode? root = VdfParser.Parse("\"AppState\" { \"AppID\" \"440\" }");

        Assert.Equal("440", root!.GetString("appstate", "appid"));
    }

    [Fact]
    public void UnescapesDoubledBackslashesInPaths()
    {
        VdfNode? root = VdfParser.Parse("""
            "libraryfolders" { "0" { "path" "D:\\SteamLibrary\\Games" } }
            """);

        Assert.Equal(@"D:\SteamLibrary\Games", root!.GetString("libraryfolders", "0", "path"));
    }

    [Fact]
    public void SkipsComments()
    {
        VdfNode? root = VdfParser.Parse("""
            // a leading comment
            "AppState"
            {
                "appid" "440" // trailing
            }
            """);

        Assert.Equal("440", root!.GetString("AppState", "appid"));
    }

    [Fact]
    public void MissingPathsReturnNullRatherThanThrowing()
    {
        VdfNode? root = VdfParser.Parse("\"AppState\" { \"appid\" \"440\" }");

        Assert.Null(root!.GetString("AppState", "nope"));
        Assert.Null(root.GetString("nothing", "here", "either"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("}}}")]
    [InlineData("orphan")]
    public void DegenerateInputReturnsNull(string? text)
    {
        // A lone token is a key with no value, and stray braces close nothing, so neither
        // produces a child and the document is reported as empty.
        Assert.Null(VdfParser.Parse(text));
    }

    [Fact]
    public void FreeTextParsesIntoPairsRatherThanFailing()
    {
        // The parser is deliberately lenient, so prose does parse - into nonsense keys.
        // Nothing downstream asks for those keys, so callers still get nothing usable.
        VdfNode? root = VdfParser.Parse("not vdf at all");

        Assert.NotNull(root);
        Assert.Null(root["AppState"]);
        Assert.Null(root.GetString("libraryfolders", "0", "path"));
    }

    [Fact]
    public void TruncatedFileYieldsWhatWasReadRatherThanThrowing()
    {
        // Steam can be mid-write when we read; a partial file must not break the scan.
        VdfNode? root = VdfParser.Parse("\"AppState\" { \"appid\" \"440\" \"name\" \"Half");

        Assert.NotNull(root);
        Assert.Equal("440", root.GetString("AppState", "appid"));
    }
}

public sealed class SteamLibraryTests
{
    private const string NewFormatLibraryFolders = """
        "libraryfolders"
        {
            "contentstatsid"        "-1234567890123456789"
            "0"
            {
                "path"      "C:\\Program Files (x86)\\Steam"
                "label"     ""
                "apps"
                {
                    "440"       "12345678"
                }
            }
            "1"
            {
                "path"      "D:\\SteamLibrary"
                "label"     ""
            }
        }
        """;

    private const string OldFormatLibraryFolders = """
        "LibraryFolders"
        {
            "TimeNextStatsReport"       "1700000000"
            "ContentStatsID"            "1234"
            "1"     "D:\\SteamLibrary"
            "2"     "E:\\Games\\Steam"
        }
        """;

    [Fact]
    public void ReadsLibraryPathsFromTheCurrentFormat()
    {
        List<string> paths = SteamLibrary.ParseLibraryFolders(NewFormatLibraryFolders);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void ReadsLibraryPathsFromTheOlderFormat()
    {
        // Older Steam wrote the path as the value directly rather than in a block.
        List<string> paths = SteamLibrary.ParseLibraryFolders(OldFormatLibraryFolders);

        Assert.Equal([@"D:\SteamLibrary", @"E:\Games\Steam"], paths);
    }

    [Fact]
    public void IgnoresBookkeepingKeysAlongsideTheLibraries()
    {
        // "contentstatsid" and "TimeNextStatsReport" are not libraries.
        Assert.DoesNotContain(
            SteamLibrary.ParseLibraryFolders(NewFormatLibraryFolders),
            p => p.Contains("stats", StringComparison.OrdinalIgnoreCase));
    }

    private static string Manifest(string appId, string name, string stateFlags = "4", string installDir = "Team Fortress 2") => $$"""
        "AppState"
        {
            "appid"         "{{appId}}"
            "Universe"      "1"
            "name"          "{{name}}"
            "StateFlags"    "{{stateFlags}}"
            "installdir"    "{{installDir}}"
            "LastUpdated"   "1700000000"
        }
        """;

    [Fact]
    public void ReadsAnInstalledGame()
    {
        GameEntry? game = SteamLibrary.ParseAppManifest(
            Manifest("440", "Team Fortress 2"),
            @"D:\SteamLibrary");

        Assert.NotNull(game);
        Assert.Equal("Team Fortress 2", game.Name);
        Assert.Equal("Steam", game.LibraryName);
        Assert.Equal("steam://rungameid/440", game.LaunchUri);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Team Fortress 2", game.InstallDirectory);
    }

    [Theory]
    [InlineData("2")]    // update queued
    [InlineData("1026")] // update running
    public void SkipsGamesThatAreNotFullyInstalled(string stateFlags)
    {
        Assert.Null(SteamLibrary.ParseAppManifest(
            Manifest("440", "Team Fortress 2", stateFlags),
            @"D:\SteamLibrary"));
    }

    [Fact]
    public void AcceptsAGameWhoseStateAlsoHasOtherBitsSet()
    {
        // 4 is "installed"; higher bits are ordinary extra state.
        Assert.NotNull(SteamLibrary.ParseAppManifest(
            Manifest("440", "Team Fortress 2", "6"),
            @"D:\SteamLibrary"));
    }

    [Fact]
    public void SkipsSharedRedistributables()
    {
        // Steam keeps this in every library; it is not a game.
        Assert.Null(SteamLibrary.ParseAppManifest(
            Manifest("228980", "Steamworks Common Redistributables"),
            @"D:\SteamLibrary"));
    }

    [Theory]
    [InlineData("Steam Linux Runtime 3.0 (sniper)")]
    [InlineData("Proton 9.0")]
    public void SkipsCompatibilityRuntimes(string name)
    {
        Assert.Null(SteamLibrary.ParseAppManifest(Manifest("999999", name), @"D:\SteamLibrary"));
    }

    [Fact]
    public void SkipsAManifestWithNoNameOrId()
    {
        Assert.Null(SteamLibrary.ParseAppManifest("\"AppState\" { \"StateFlags\" \"4\" }", @"D:\S"));
    }

    [Fact]
    public void SkipsAnUnparseableManifest()
    {
        Assert.Null(SteamLibrary.ParseAppManifest("this is not a manifest", @"D:\S"));
    }
}
