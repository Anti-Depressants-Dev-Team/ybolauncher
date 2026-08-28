using Launcher.Core.Discovery.Games;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Amazon Games. The registry walk itself needs an installed launcher, so the entry-to-game
/// step is driven directly with the values the registry would supply, against real folders
/// on disk.
/// </summary>
public sealed class AmazonGamesLibraryTests : IDisposable
{
    private const string Uninstall =
        "\"C:\\Amazon Games\\App\\Amazon Game Remover.exe\" -m Game -p amzn1.adg.product.abc123";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Install(string folderName, string executableName, string? fuel = null)
    {
        string folder = Path.Combine(_temp.Path, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, executableName), new byte[2048]);

        if (fuel is not null)
        {
            File.WriteAllText(Path.Combine(folder, "fuel.json"), fuel);
        }

        return folder;
    }

    [Fact]
    public void ReadsAGameAndItsLaunchProtocol()
    {
        string folder = Install("Lost Ark", "LostArk.exe");

        GameEntry? game = AmazonGamesLibrary.BuildGame("Lost Ark", folder, Uninstall);

        Assert.NotNull(game);
        Assert.Equal("Lost Ark", game.Name);
        Assert.Equal("Amazon Games", game.LibraryName);
        Assert.Equal("amazon-games://play/amzn1.adg.product.abc123", game.LaunchUri);
        Assert.Equal(folder, game.InstallDirectory);
    }

    [Fact]
    public void PrefersTheExecutableNamedInFuelJson()
    {
        // The launcher records the real entry point, which is not always the biggest exe.
        string folder = Install(
            "New World",
            "Launch.exe",
            """{ "Main": { "Command": "Launch.exe", "Args": ["-nolauncher"] } }""");

        File.WriteAllBytes(Path.Combine(folder, "BigEngineBinary.exe"), new byte[64 * 1024]);

        GameEntry? game = AmazonGamesLibrary.BuildGame("New World", folder, Uninstall);

        Assert.Equal(Path.Combine(folder, "Launch.exe"), game!.ExecutablePath);
    }

    [Fact]
    public void SearchesTheFolderWhenFuelJsonIsMissingOrWrong()
    {
        string folder = Install("Old Game", "OldGame.exe", """{ "Main": { "Command": "gone.exe" } }""");

        GameEntry? game = AmazonGamesLibrary.BuildGame("Old Game", folder, Uninstall);

        Assert.Equal(Path.Combine(folder, "OldGame.exe"), game!.ExecutablePath);
    }

    [Fact]
    public void TheLauncherItselfIsNotAGame()
    {
        Assert.Null(AmazonGamesLibrary.BuildGame("Amazon Games", _temp.Path, Uninstall));
    }

    [Fact]
    public void AnEntryWithNoProductIdAndNothingToRunIsSkipped()
    {
        Assert.Null(AmazonGamesLibrary.BuildGame("Ghost", @"Z:\gone", "some-uninstaller.exe"));
    }

    [Fact]
    public void AGameStillCountsWhenOnlyTheProtocolIsKnown()
    {
        // An install folder on a drive that is not mounted right now.
        GameEntry? game = AmazonGamesLibrary.BuildGame("Lost Ark", @"Z:\gone", Uninstall);

        Assert.Equal("amazon-games://play/amzn1.adg.product.abc123", game!.LaunchUri);
        Assert.Null(game.ExecutablePath);
    }

    [Theory]
    [InlineData("\"C:\\x\\Amazon Game Remover.exe\" -m Game -p amzn1.adg.product.abc123", "amzn1.adg.product.abc123")]
    [InlineData("remover.exe -p \"quoted-id\"", "quoted-id")]
    [InlineData("remover.exe -m Game", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ReadsTheProductIdFromTheUninstallCommand(string? uninstall, string? expected)
    {
        Assert.Equal(expected, AmazonGamesLibrary.ParseProductId(uninstall));
    }

    [Theory]
    [InlineData("""{ "Main": { "Command": "Game.exe" } }""", "Game.exe")]
    [InlineData("""{ "Main": { "Command": "bin/Game.exe" } }""", "bin/Game.exe")]
    [InlineData("""{ "Main": {} }""", null)]
    [InlineData("{}", null)]
    [InlineData("not json", null)]
    public void ReadsTheCommandFromFuelJson(string json, string? expected)
    {
        Assert.Equal(expected, AmazonGamesLibrary.ParseFuelCommand(json));
    }

    [Fact]
    public void AnAbsentLauncherReportsNothing()
    {
        // No Amazon uninstall keys on this machine.
        Assert.Empty(new AmazonGamesLibrary().Enumerate());
    }
}

/// <summary>
/// Rockstar Games Launcher, driven the same way: real folders, registry values supplied
/// directly.
/// </summary>
public sealed class RockstarLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Install(string folderName, params string[] executables)
    {
        string folder = Path.Combine(_temp.Path, folderName);
        Directory.CreateDirectory(folder);

        for (int i = 0; i < executables.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(folder, executables[i]), new byte[2048 * (i + 1)]);
        }

        return folder;
    }

    [Fact]
    public void ReadsAnInstalledTitle()
    {
        string folder = Install("Grand Theft Auto V", "PlayGTAV.exe");

        GameEntry? game = RockstarLibrary.BuildGame("Grand Theft Auto V", folder);

        Assert.NotNull(game);
        Assert.Equal("Grand Theft Auto V", game.Name);
        Assert.Equal("Rockstar Games", game.LibraryName);
        Assert.Equal(Path.Combine(folder, "PlayGTAV.exe"), game.ExecutablePath);

        // The game's own boot executable brings up the launcher, so there is no protocol.
        Assert.Null(game.LaunchUri);
    }

    [Fact]
    public void TrimsTheTrailingSeparatorTheRegistryValueCarries()
    {
        string folder = Install("Max Payne 3", "MaxPayne3.exe");

        GameEntry? game = RockstarLibrary.BuildGame("Max Payne 3", folder + @"\");

        Assert.Equal(folder, game!.InstallDirectory);
    }

    [Theory]
    [InlineData("Launcher")]
    [InlineData("Rockstar Games Launcher")]
    [InlineData("Social Club")]
    [InlineData("Rockstar Games Social Club")]
    public void KeysUnderTheSameRootThatAreNotGamesAreSkipped(string title)
    {
        Assert.Null(RockstarLibrary.BuildGame(title, Install(title, "thing.exe")));
    }

    [Fact]
    public void ATitleWithNoFolderOrNoExecutableIsSkipped()
    {
        Assert.Null(RockstarLibrary.BuildGame("Bully", null));
        Assert.Null(RockstarLibrary.BuildGame("Bully", @"Z:\gone"));

        // A folder with only an uninstaller in it has nothing to launch.
        Assert.Null(RockstarLibrary.BuildGame("Bully", Install("Bully", "uninstall.exe")));
    }

    [Fact]
    public void AnAbsentLauncherReportsNothing()
    {
        Assert.Empty(new RockstarLibrary().Enumerate());
    }
}
