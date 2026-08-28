using Launcher.Core.Discovery;
using Launcher.Core.Discovery.Games;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class EpicLibraryTests
{
    private const string FortniteManifest = """
        {
            "FormatVersion": 0,
            "bIsIncompleteInstall": false,
            "AppName": "Fortnite",
            "CatalogNamespace": "fn",
            "CatalogItemId": "4fe75bbc5a674f4f9b356b5c90567da5",
            "DisplayName": "Fortnite",
            "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
            "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteClient-Win64-Shipping.exe",
            "AppCategories": ["games", "applications"]
        }
        """;

    [Fact]
    public void ReadsAnInstalledGame()
    {
        GameEntry? game = EpicLibrary.ParseManifest(FortniteManifest);

        Assert.NotNull(game);
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal("Epic Games", game.LibraryName);
        Assert.Equal(@"C:\Program Files\Epic Games\Fortnite", game.InstallDirectory);
    }

    [Fact]
    public void BuildsTheFullyQualifiedLaunchUri()
    {
        // The launcher's own shortcuts use namespace:catalogItem:appName, percent-encoded.
        GameEntry? game = EpicLibrary.ParseManifest(FortniteManifest);

        Assert.Equal(
            "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
            game!.LaunchUri);
    }

    [Fact]
    public void FallsBackToTheShortLaunchUriWhenCatalogFieldsAreMissing()
    {
        GameEntry? game = EpicLibrary.ParseManifest("""
            { "AppName": "Solo", "DisplayName": "Solo Game", "InstallLocation": "C:\\Games\\Solo" }
            """);

        Assert.Equal("com.epicgames.launcher://apps/Solo?action=launch&silent=true", game!.LaunchUri);
    }

    [Fact]
    public void JoinsTheInstallLocationAndLaunchExecutable()
    {
        GameEntry? game = EpicLibrary.ParseManifest(FortniteManifest);

        // Epic writes the executable with forward slashes.
        Assert.Equal(
            @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe",
            game!.ExecutablePath);
    }

    [Fact]
    public void SkipsAnIncompleteInstall()
    {
        // Epic writes a manifest as soon as a download starts.
        Assert.Null(EpicLibrary.ParseManifest("""
            {
                "bIsIncompleteInstall": true,
                "AppName": "Halfway",
                "DisplayName": "Halfway Downloaded",
                "AppCategories": ["games"]
            }
            """));
    }

    [Fact]
    public void SkipsNonGameEntries()
    {
        // The launcher tracks engine installs and plugins in the same folder.
        Assert.Null(EpicLibrary.ParseManifest("""
            {
                "AppName": "UE_5.4",
                "DisplayName": "Unreal Engine",
                "AppCategories": ["engines"]
            }
            """));
    }

    [Fact]
    public void AcceptsOlderManifestsWithNoCategories()
    {
        Assert.NotNull(EpicLibrary.ParseManifest("""
            { "AppName": "Old", "DisplayName": "Old Game" }
            """));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"AppName\": \"NoDisplayName\" }")]
    public void DegenerateManifestsAreSkipped(string json)
    {
        Assert.Null(EpicLibrary.ParseManifest(json));
    }
}

public sealed class EaLibraryTests
{
    [Fact]
    public void ReadsTheOfferIdFromAManifest()
    {
        string manifest = "?currentstate=kCompleted&previousstate=kPostTransfer"
            + "&ddinstallalreadycompleted=0&id=OFB-EAST%3a109552316&installpath=C%3a%5cGames";

        // The id is stored percent-encoded; the launch protocol wants it decoded.
        Assert.Equal("OFB-EAST:109552316", EaLibrary.ParseOfferId(manifest));
    }

    [Fact]
    public void HandlesAManifestWithoutTheLeadingQuestionMark()
    {
        Assert.Equal("1234", EaLibrary.ParseOfferId("currentstate=kCompleted&id=1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("currentstate=kCompleted&installpath=C%3a")]
    [InlineData("id=")]
    [InlineData("no pairs here")]
    public void ReturnsNullWhenThereIsNoUsableId(string manifest)
    {
        Assert.Null(EaLibrary.ParseOfferId(manifest));
    }
}

public sealed class GameLibraryIntegrationTests
{
    [Fact]
    public void AnUninstalledLauncherReportsNothingRatherThanThrowing()
    {
        // Every library must be safe to run on a machine where that launcher is absent,
        // which is the normal case for most of them.
        IGameLibrary[] libraries =
        [
            new SteamLibrary(() => null),
            new EpicLibrary(() => null),
            new EaLibrary(() => []),
            new GogLibrary(),
            new UbisoftLibrary(),
            new BattleNetLibrary(),
            new ItchLibrary(() => null),
            new GameJoltLibrary(() => null),
            new AmazonGamesLibrary(),
            new RockstarLibrary(),
            new HoYoPlayLibrary(),
            new RiotLibrary(() => null),
        ];

        foreach (IGameLibrary library in libraries)
        {
            IReadOnlyList<GameEntry> games = library.Enumerate();
            Assert.NotNull(games);
        }
    }

    [Fact]
    public void SteamPointedAtAMissingFolderReportsNothing()
    {
        Assert.Empty(new SteamLibrary(() => @"Z:\no\steam\here").Enumerate());
    }

    [Fact]
    public void AGameLaunchUriSurvivesTheJunkFilter()
    {
        // steam:// is not a web link, so the filter must keep it.
        var entry = new AppEntry
        {
            DisplayName = "Team Fortress 2",
            OriginalName = "Team Fortress 2",
            Source = AppSource.GameLauncher,
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "steam://rungameid/440",
        };

        Assert.Equal(FilterReason.None, new JunkFilter(_ => true).Evaluate(entry));
    }

    [Fact]
    public void AGameAndItsStartMenuShortcutMergeIntoOneEntry()
    {
        // Steam writes a .url shortcut holding the same steam:// URI, so the game must not
        // appear twice.
        var fromLibrary = new AppEntry
        {
            DisplayName = "Team Fortress 2",
            OriginalName = "Team Fortress 2",
            Source = AppSource.GameLauncher,
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "steam://rungameid/440",
        };

        var fromStartMenu = new AppEntry
        {
            DisplayName = "Team Fortress 2",
            OriginalName = "Team Fortress 2",
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "steam://rungameid/440",
            ShortcutPath = @"C:\Menu\Team Fortress 2.url",
        };

        foreach (AppEntry entry in new[] { fromLibrary, fromStartMenu })
        {
            entry.MergeKey = AppIdentity.ForEntry(entry);
        }

        List<AppEntry> merged = AppDeduplicator.Merge([fromLibrary, fromStartMenu]);

        Assert.Single(merged);
        Assert.Equal(@"C:\Menu\Team Fortress 2.url", merged[0].ShortcutPath);
    }
}
