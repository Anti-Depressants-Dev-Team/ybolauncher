using Launcher.Core.Discovery.Games;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// HoYoPlay. The registry walk needs an installed launcher, so the path-to-game step is
/// driven directly with the values the registry would supply, against real folders.
/// </summary>
public sealed class HoYoPlayLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Writes a folder holding the given files, and returns it.</summary>
    private string Install(string folderName, params string[] files)
    {
        string folder = Path.Combine(_temp.Path, folderName);
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
    public void ReadsAGameFromItsBuildFolder()
    {
        // HoYoPlay records the parent; the executable is one level down.
        string folder = Install("Genshin Impact", @"Genshin Impact game\GenshinImpact.exe");

        GameEntry? game = HoYoPlayLibrary.BuildGame(folder, null);

        Assert.NotNull(game);
        Assert.Equal("Genshin Impact", game.Name);
        Assert.Equal("HoYoPlay", game.LibraryName);
        Assert.Equal(Path.Combine(folder, "Genshin Impact game", "GenshinImpact.exe"), game.ExecutablePath);

        // These executables bring up their own sign-in; there is no launcher protocol.
        Assert.Null(game.LaunchUri);
    }

    [Fact]
    public void ReadsAGameWhoseFolderIsTheBuildFolder()
    {
        string folder = Install("Star Rail", "StarRail.exe");

        Assert.Equal("Honkai: Star Rail", HoYoPlayLibrary.BuildGame(folder, null)!.Name);
    }

    [Theory]
    [InlineData("GenshinImpact.exe", "Genshin Impact")]
    [InlineData("YuanShen.exe", "Genshin Impact")]
    [InlineData("StarRail.exe", "Honkai: Star Rail")]
    [InlineData("BH3.exe", "Honkai Impact 3rd")]
    [InlineData("ZenlessZoneZero.exe", "Zenless Zone Zero")]
    public void NamesAGameAfterItsExecutableRatherThanItsFolder(string executable, string expected)
    {
        // The folder is named after the build, and the Chinese clients use different
        // executable names for the same game.
        string folder = Install(executable + "-install", executable);

        Assert.Equal(expected, HoYoPlayLibrary.BuildGame(folder, "Some Registry Name")!.Name);
    }

    [Fact]
    public void AGameThisBuildDoesNotKnowStillGetsATile()
    {
        string folder = Install("Future Game", "FutureGame.exe");

        GameEntry? game = HoYoPlayLibrary.BuildGame(folder, "Future Game");

        Assert.Equal("Future Game", game!.Name);
        Assert.Equal(Path.Combine(folder, "FutureGame.exe"), game.ExecutablePath);
    }

    [Fact]
    public void PrefersTheKnownExecutableOverTheLargestOne()
    {
        string folder = Install("Genshin Impact", "GenshinImpact.exe");
        File.WriteAllBytes(Path.Combine(folder, "UnityPlayer.exe"), new byte[256 * 1024]);

        Assert.EndsWith("GenshinImpact.exe", HoYoPlayLibrary.BuildGame(folder, null)!.ExecutablePath);
    }

    [Theory]
    [InlineData("HoYoPlay")]
    [InlineData("miHoYo Launcher")]
    public void TheLauncherItselfIsNotAGame(string displayName)
    {
        string folder = Install("HoYoPlay", "launcher.exe");

        Assert.Null(HoYoPlayLibrary.BuildGame(folder, displayName));
    }

    [Fact]
    public void AnInstallPathWithNothingInItIsSkipped()
    {
        Assert.Null(HoYoPlayLibrary.BuildGame(null, "Genshin Impact"));
        Assert.Null(HoYoPlayLibrary.BuildGame(@"Z:\gone", "Genshin Impact"));
        Assert.Null(HoYoPlayLibrary.BuildGame(Install("Empty"), "Genshin Impact"));
    }

    [Fact]
    public void TrimsQuotesAndTrailingSeparatorsFromTheRegistryValue()
    {
        string folder = Install("Star Rail", "StarRail.exe");

        Assert.NotNull(HoYoPlayLibrary.BuildGame("\"" + folder + "\\\"", null));
    }

    [Fact]
    public void AnAbsentLauncherReportsNothing()
    {
        // No HoYoverse keys on this machine.
        Assert.Empty(new HoYoPlayLibrary().Enumerate());
    }
}
