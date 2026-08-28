using System.IO.Compression;
using System.Text;
using Launcher.Core.Discovery.Games;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// itch.io, driven over a real install layout on disk: neither the app nor a game is
/// present on the development machine, so the folders are written by the test.
/// </summary>
public sealed class ItchLibraryTests : IDisposable
{
    private const string CelesteReceipt = """
        {
            "game": {
                "id": 12345,
                "title": "Celeste Classic",
                "classification": "game",
                "url": "https://example.itch.io/celeste-classic"
            },
            "upload": { "id": 999, "displayName": "Windows build" },
            "installerName": "archive"
        }
        """;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Writes one install folder: an executable plus a gzipped receipt.</summary>
    private static string Install(string root, string folderName, string receipt, string executableName)
    {
        string folder = Path.Combine(root, "apps", folderName);
        Directory.CreateDirectory(Path.Combine(folder, ".itch"));
        File.WriteAllBytes(Path.Combine(folder, executableName), new byte[2048]);

        using FileStream file = File.Create(Path.Combine(folder, ".itch", "receipt.json.gz"));
        using (var compressor = new GZipStream(file, CompressionLevel.Optimal))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(receipt);
            compressor.Write(bytes, 0, bytes.Length);
        }

        return folder;
    }

    [Fact]
    public void ReadsAnInstalledGameFromItsGzippedReceipt()
    {
        Install(_temp.Path, "celeste-classic", CelesteReceipt, "Celeste.exe");

        GameEntry game = Assert.Single(new ItchLibrary(() => _temp.Path).Enumerate());

        Assert.Equal("Celeste Classic", game.Name);
        Assert.Equal("itch.io", game.LibraryName);
        Assert.EndsWith(@"celeste-classic\Celeste.exe", game.ExecutablePath);

        // itch downloads are DRM-free, so there is no protocol to go through.
        Assert.Null(game.LaunchUri);
    }

    [Fact]
    public void SkipsDownloadsThatAreNotSomethingToLaunch()
    {
        // itch hosts art packs, soundtracks and books alongside games.
        Install(
            _temp.Path,
            "pixel-pack",
            """{ "game": { "title": "Pixel Art Pack", "classification": "assets" } }""",
            "viewer.exe");

        Assert.Empty(new ItchLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void SkipsAnInstallWithNothingToRun()
    {
        // A web game runs inside the itch app itself, which this cannot drive.
        string folder = Path.Combine(_temp.Path, "apps", "web-game");
        Directory.CreateDirectory(Path.Combine(folder, ".itch"));
        File.WriteAllText(Path.Combine(folder, ".itch", "receipt.json"), CelesteReceipt);
        File.WriteAllText(Path.Combine(folder, "index.html"), "<html></html>");

        Assert.Empty(new ItchLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void AFolderWithNoReceiptIsNotAnItchInstall()
    {
        string folder = Path.Combine(_temp.Path, "apps", "something-else");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "app.exe"), new byte[512]);

        Assert.Empty(new ItchLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void ReadsAPlainReceiptWhenThereIsNoGzippedOne()
    {
        string folder = Path.Combine(_temp.Path, "apps", "plain");
        Directory.CreateDirectory(Path.Combine(folder, ".itch"));
        File.WriteAllText(Path.Combine(folder, ".itch", "receipt.json"), CelesteReceipt);
        File.WriteAllBytes(Path.Combine(folder, "Celeste.exe"), new byte[512]);

        Assert.Single(new ItchLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void FallsBackToTheFolderNameWhenTheReceiptHasNoTitle()
    {
        Install(_temp.Path, "mystery-game", """{ "installerName": "archive" }""", "run.exe");

        GameEntry game = Assert.Single(new ItchLibrary(() => _temp.Path).Enumerate());

        Assert.Equal("mystery-game", game.Name);
    }

    [Fact]
    public void ReadsGamesInstalledOutsideTheDefaultFolder()
    {
        // The user can add install locations on other drives.
        string other = Path.Combine(_temp.Path, "elsewhere");
        Install(_temp.Path, "here", CelesteReceipt, "Here.exe");
        Install(other, "there", """{ "game": { "title": "Other Drive Game" } }""", "There.exe");

        File.WriteAllText(
            Path.Combine(_temp.Path, "preferences.json"),
            $$"""
            { "installLocations": [ { "id": "extra", "path": "{{Path.Combine(other, "apps").Replace(@"\", @"\\")}}" } ] }
            """);

        Assert.Equal(
            ["Celeste Classic", "Other Drive Game"],
            new ItchLibrary(() => _temp.Path).Enumerate().Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("""{ "installLocations": ["D:\\itch"] }""")]
    [InlineData("""{ "installLocations": [{ "path": "D:\\itch" }] }""")]
    [InlineData("""{ "installLocations": { "extra": { "path": "D:\\itch" } } }""")]
    [InlineData("""{ "installLocations": { "extra": "D:\\itch" } }""")]
    [InlineData("""{ "downloadLocations": ["D:\\itch"] }""")]
    public void AcceptsEveryShapeThePreferencesFileHasUsed(string json)
    {
        Assert.Equal([@"D:\itch"], ItchLibrary.ParseInstallLocations(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void DegeneratePreferencesYieldNoExtraLocations(string json)
    {
        Assert.Empty(ItchLibrary.ParseInstallLocations(json));
    }

    [Fact]
    public void AnAbsentAppReportsNothing()
    {
        Assert.Empty(new ItchLibrary(() => null).Enumerate());
        Assert.Empty(new ItchLibrary(() => @"Z:\no\itch\here").Enumerate());
    }
}

/// <summary>
/// Game Jolt, driven over its client data store written to disk.
/// </summary>
public sealed class GameJoltLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string InstallFolder(string name, string executableName)
    {
        string folder = Path.Combine(_temp.Path, "Games", name);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, executableName), new byte[2048]);

        return folder;
    }

    private void WriteStore(string name, string json) =>
        File.WriteAllText(Path.Combine(_temp.Path, name + ".wttf"), json);

    [Fact]
    public void ReadsAnInstalledPackageAndNamesItAfterItsGame()
    {
        string folder = InstallFolder("nomad", "Nomad.exe");

        WriteStore("games", """{ "77": { "id": 77, "title": "Nomad Fleet" } }""");
        WriteStore(
            "packages",
            $$"""
            {
                "101": {
                    "id": 101,
                    "game_id": 77,
                    "title": "Windows Build",
                    "install_dir": "{{folder.Replace(@"\", @"\\")}}",
                    "install_state": null,
                    "launch_options": [ { "id": 5, "executable_path": "Nomad.exe" } ]
                }
            }
            """);

        GameEntry game = Assert.Single(new GameJoltLibrary(() => _temp.Path).Enumerate());

        // The package is named after the build; the game is what the user recognises.
        Assert.Equal("Nomad Fleet", game.Name);
        Assert.Equal("Game Jolt", game.LibraryName);
        Assert.Equal(Path.Combine(folder, "Nomad.exe"), game.ExecutablePath);
        Assert.Null(game.LaunchUri);
    }

    [Fact]
    public void FindsTheExecutableWhenTheRecordedOneIsMissing()
    {
        string folder = InstallFolder("drifter", "Drifter.exe");

        WriteStore(
            "packages",
            $$"""
            [ {
                "id": 1,
                "title": "Drifter",
                "install_dir": "{{folder.Replace(@"\", @"\\")}}",
                "launch_options": [ { "executable_path": "moved/Drifter.exe" } ]
            } ]
            """);

        GameEntry game = Assert.Single(new GameJoltLibrary(() => _temp.Path).Enumerate());

        Assert.Equal(Path.Combine(folder, "Drifter.exe"), game.ExecutablePath);
    }

    [Fact]
    public void SkipsAPackageThatIsStillInstalling()
    {
        string folder = InstallFolder("halfway", "Halfway.exe");

        WriteStore(
            "packages",
            $$"""
            [ {
                "id": 2,
                "title": "Halfway",
                "install_dir": "{{folder.Replace(@"\", @"\\")}}",
                "install_state": "installing"
            } ]
            """);

        Assert.Empty(new GameJoltLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void SkipsAPackageWhoseFolderIsGone()
    {
        // The client leaves the record behind when a game is deleted from disk.
        WriteStore(
            "packages",
            """[ { "id": 3, "title": "Deleted", "install_dir": "Z:\\gone\\Deleted" } ]""");

        Assert.Empty(new GameJoltLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void SkipsARemovedPackage()
    {
        string folder = InstallFolder("removed", "Removed.exe");

        WriteStore(
            "packages",
            $$"""
            [ { "id": 4, "title": "Removed", "is_removed": true, "install_dir": "{{folder.Replace(@"\", @"\\")}}" } ]
            """);

        Assert.Empty(new GameJoltLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void AcceptsAStoreWrappedInAContainerProperty()
    {
        string folder = InstallFolder("wrapped", "Wrapped.exe");

        WriteStore(
            "packages",
            $$"""
            { "objects": { "9": { "id": 9, "title": "Wrapped", "install_dir": "{{folder.Replace(@"\", @"\\")}}" } } }
            """);

        Assert.Single(new GameJoltLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void FallsBackToThePackageTitleWhenThereIsNoGameRecord()
    {
        string folder = InstallFolder("orphan", "Orphan.exe");

        WriteStore(
            "packages",
            $$"""
            [ { "id": 6, "game_id": 404, "title": "Orphan Package", "install_dir": "{{folder.Replace(@"\", @"\\")}}" } ]
            """);

        Assert.Equal("Orphan Package", Assert.Single(new GameJoltLibrary(() => _temp.Path).Enumerate()).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""[ { "id": 1, "title": "No Folder" } ]""")]
    public void DegenerateStoresYieldNoGames(string json)
    {
        Assert.Empty(GameJoltLibrary.ParsePackages(json, null));
    }

    [Fact]
    public void AnAbsentClientReportsNothing()
    {
        Assert.Empty(new GameJoltLibrary(() => null).Enumerate());
        Assert.Empty(new GameJoltLibrary(() => @"Z:\no\gamejolt\here").Enumerate());

        // Present but with no store file yet - a fresh install.
        Assert.Empty(new GameJoltLibrary(() => _temp.Path).Enumerate());
    }
}
