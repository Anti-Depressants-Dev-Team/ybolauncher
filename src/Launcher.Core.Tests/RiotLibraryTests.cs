using Launcher.Core.Discovery.Games;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Riot Games, driven over the ProgramData layout the client writes.
/// </summary>
public sealed class RiotLibraryTests : IDisposable
{
    private const string ClientPath = @"C:\Riot Games\Riot Client\RiotClientServices.exe";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Writes the shared client record and one metadata folder per product.</summary>
    private void WriteRiotLayout(params (string Id, string InstallPath)[] products)
    {
        File.WriteAllText(
            Path.Combine(_temp.Path, "RiotClientInstalls.json"),
            $$"""
            {
                "rc_default": "{{ClientPath.Replace(@"\", @"\\")}}",
                "rc_live": "{{ClientPath.Replace(@"\", @"\\")}}"
            }
            """);

        foreach ((string id, string installPath) in products)
        {
            string folder = Path.Combine(_temp.Path, "Metadata", id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(
                Path.Combine(folder, id + ".product_settings.yaml"),
                $"product_install_full_path: {installPath}\nproduct_install_root: whatever\n");
        }
    }

    private string GameFolder(string name, params string[] files)
    {
        string folder = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(folder);

        foreach (string file in files)
        {
            string full = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[4096]);
        }

        return folder;
    }

    [Fact]
    public void ReadsEveryInstalledProduct()
    {
        WriteRiotLayout(
            ("league_of_legends.live", GameFolder("League of Legends", "LeagueClient.exe")),
            ("valorant.live", GameFolder("VALORANT", @"live\VALORANT.exe")));

        IReadOnlyList<GameEntry> games = new RiotLibrary(() => _temp.Path).Enumerate();

        Assert.Equal(
            ["League of Legends", "VALORANT"],
            games.Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryGameStartsThroughTheOneClientWithItsOwnSwitches()
    {
        WriteRiotLayout(("league_of_legends.live", GameFolder("League of Legends", "LeagueClient.exe")));

        GameEntry game = Assert.Single(new RiotLibrary(() => _temp.Path).Enumerate());

        Assert.Equal(ClientPath, game.ExecutablePath);
        Assert.Equal("--launch-product=league_of_legends --launch-patchline=live", game.Arguments);
        Assert.Null(game.LaunchUri);
    }

    [Fact]
    public void TakesTheIconFromTheGameRatherThanTheSharedClient()
    {
        string folder = GameFolder("League of Legends", "LeagueClient.exe");

        WriteRiotLayout(("league_of_legends.live", folder));

        GameEntry game = Assert.Single(new RiotLibrary(() => _temp.Path).Enumerate());

        // Without this every Riot tile would show the Riot Client icon.
        Assert.Equal(Path.Combine(folder, "LeagueClient.exe"), game.IconPath);
    }

    [Fact]
    public void FindsAnIconOneLevelDownForAPatchlineFolder()
    {
        string folder = GameFolder("VALORANT", @"live\VALORANT.exe");

        WriteRiotLayout(("valorant.live", folder));

        GameEntry game = Assert.Single(new RiotLibrary(() => _temp.Path).Enumerate());

        Assert.Equal(Path.Combine(folder, "live", "VALORANT.exe"), game.IconPath);
    }

    [Fact]
    public void MarksANonLivePatchlineSoItIsNotConfusedWithTheRealGame()
    {
        GameEntry? game = RiotLibrary.BuildGame("league_of_legends.pbe", null, ClientPath);

        Assert.Equal("League of Legends (PBE)", game!.Name);
        Assert.Equal("--launch-product=league_of_legends --launch-patchline=pbe", game.Arguments);
    }

    [Fact]
    public void UsesThePublishedNameForAKnownProductId()
    {
        // "bacon" is what Riot calls Legends of Runeterra internally.
        Assert.Equal("Legends of Runeterra", RiotLibrary.BuildGame("bacon.live", null, ClientPath)!.Name);
    }

    [Fact]
    public void AProductThisBuildDoesNotKnowStillGetsATile()
    {
        Assert.Equal("some new game", RiotLibrary.BuildGame("some_new_game.live", null, ClientPath)!.Name);
    }

    [Fact]
    public void TheClientItselfIsNotAGame()
    {
        Assert.Null(RiotLibrary.BuildGame("riot_client.live", null, ClientPath));
    }

    [Theory]
    [InlineData("""{ "rc_default": "C:\\Riot\\RiotClientServices.exe" }""", @"C:\Riot\RiotClientServices.exe")]
    [InlineData("""{ "rc_live": "C:\\Riot\\RiotClientServices.exe" }""", @"C:\Riot\RiotClientServices.exe")]
    [InlineData("""{ "associated_client": {} }""", null)]
    [InlineData("not json", null)]
    [InlineData("[]", null)]
    public void ReadsTheClientPathFromTheInstallsFile(string json, string? expected)
    {
        Assert.Equal(expected, RiotLibrary.ParseClientPath(json));
    }

    [Theory]
    [InlineData("product_install_full_path: C:/Riot Games/VALORANT", @"C:\Riot Games\VALORANT")]
    [InlineData("  product_install_full_path: \"C:\\\\Riot\"  ", @"C:\\Riot")]
    [InlineData("product_install_root: C:/Riot Games", null)]
    [InlineData("", null)]
    public void ReadsTheInstallPathFromTheProductSettings(string yaml, string? expected)
    {
        Assert.Equal(expected, RiotLibrary.ParseInstallPath(yaml));
    }

    [Fact]
    public void NoClientRecordMeansNoGames()
    {
        // Metadata without a client is not something that can be launched.
        Directory.CreateDirectory(Path.Combine(_temp.Path, "Metadata", "league_of_legends.live"));

        Assert.Empty(new RiotLibrary(() => _temp.Path).Enumerate());
    }

    [Fact]
    public void AnAbsentLauncherReportsNothing()
    {
        Assert.Empty(new RiotLibrary(() => null).Enumerate());
        Assert.Empty(new RiotLibrary(() => @"Z:\no\riot\here").Enumerate());
    }
}
