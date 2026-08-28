using Launcher.Core.Discovery;
using Launcher.Core.Discovery.Games;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// The Games tab is built from <see cref="AppEntry.IsGame"/> rather than
/// <see cref="AppEntry.Source"/>, because a merge names whichever discovery route won and
/// for a game with a Start Menu shortcut that is routinely the shortcut.
/// </summary>
public sealed class GameEntryFlagTests
{
    private static AppEntry Keyed(AppEntry entry)
    {
        entry.MergeKey = AppIdentity.ForEntry(entry);
        entry.Id = AppIdentity.ToId(entry.MergeKey);

        return entry;
    }

    private static AppEntry Game(string name, string? uri, string target) =>
        Keyed(new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.GameLauncher,
            IsGame = true,
            LaunchKind = uri is null ? LaunchKind.Executable : LaunchKind.Uri,
            LaunchUri = uri,
            TargetPath = target,
        });

    private static AppEntry Shortcut(string name, string? uri, string target) =>
        Keyed(new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            LaunchKind = uri is null ? LaunchKind.Executable : LaunchKind.Uri,
            LaunchUri = uri,
            TargetPath = target,
            ShortcutPath = @"C:\Menu\" + name + ".lnk",
        });

    [Fact]
    public void ASteamGameStaysAGameAfterMergingWithItsShortcut()
    {
        // Steam's Start Menu entry is a .url holding the same URI, so the two merge on the
        // key. Whichever one the merge picks to launch by, it is still a game - this is
        // what put only HoYoPlay titles in the Games tab.
        AppEntry merged = Assert.Single(AppDeduplicator.Merge(
        [
            Shortcut("Team Fortress 2", "steam://rungameid/440", @"C:\Menu\tf2.url"),
            Game("Team Fortress 2", "steam://rungameid/440", @"D:\Steam\tf2"),
        ]));

        Assert.True(merged.IsGame);
    }

    [Fact]
    public void TheFlagSurvivesWhicheverOrderTheSourcesFinishIn()
    {
        // Sources run concurrently, so the order within a group is not fixed.
        foreach (AppEntry[] order in new[]
        {
            new[] { Game("Portal 2", "steam://rungameid/620", @"D:\Steam\p2"), Shortcut("Portal 2", "steam://rungameid/620", @"C:\Menu\p2.url") },
            new[] { Shortcut("Portal 2", "steam://rungameid/620", @"C:\Menu\p2.url"), Game("Portal 2", "steam://rungameid/620", @"D:\Steam\p2") },
        })
        {
            Assert.True(Assert.Single(AppDeduplicator.Merge(order)).IsGame);
        }
    }

    [Fact]
    public void AnOrdinaryAppIsNotMarkedAsAGame()
    {
        Assert.False(Assert.Single(AppDeduplicator.Merge([Shortcut("Notepad", null, @"C:\Windows\notepad.exe")])).IsGame);
    }

    [Fact]
    public void ARescanKeepsAnEntryMarkedAsAGame()
    {
        var stored = new AppEntry { DisplayName = "Game", OriginalName = "Game" };

        stored.UpdateFromScan(new AppEntry { DisplayName = "Game", OriginalName = "Game", IsGame = true });

        Assert.True(stored.IsGame);
    }
}

/// <summary>
/// HoYoPlay records its own install path beside the games', so the launcher itself was
/// turning up as a tile in the Games tab.
/// </summary>
public sealed class HoYoPlayLauncherFilterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Install(string folderName, string executable)
    {
        string folder = Path.Combine(_temp.Path, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, executable), new byte[4096]);

        return folder;
    }

    [Theory]
    [InlineData("launcher.exe")]
    [InlineData("HYP.exe")]
    [InlineData("HoYoPlay.exe")]
    public void TheLauncherIsNotAGameWhateverItsFolderIsCalled(string executable)
    {
        // The name check alone missed this: the HYP key gives a path and no name at all.
        string folder = Install("HoYoPlay", executable);

        Assert.Null(HoYoPlayLibrary.BuildGame(folder, null));
    }

    [Fact]
    public void ARealGameIsStillFound()
    {
        string folder = Install("Genshin Impact game", "GenshinImpact.exe");

        Assert.Equal("Genshin Impact", HoYoPlayLibrary.BuildGame(folder, null)!.Name);
    }
}
