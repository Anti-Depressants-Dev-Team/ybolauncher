using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// A launcher whose games run directly writes a Start Menu shortcut that goes through the
/// launcher's own executable, which is a different target from the game and so a different
/// merge key. Without collapsing those the game appears twice.
/// </summary>
public sealed class GameShortcutMergeTests
{
    private static AppEntry Game(string name, string executable, string? launchUri = null) =>
        Keyed(new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.GameLauncher,
            IsGame = true,
            LaunchKind = launchUri is null ? LaunchKind.Executable : LaunchKind.Uri,
            LaunchUri = launchUri,
            TargetPath = executable,
            IconCacheFile = "game-icon.png",
        });

    private static AppEntry Shortcut(string name, string target, string? arguments = null) =>
        Keyed(new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.Executable,
            TargetPath = target,
            Arguments = arguments,
            ShortcutPath = @"C:\Menu\" + name + ".lnk",
        });

    private static AppEntry Keyed(AppEntry entry)
    {
        entry.MergeKey = AppIdentity.ForEntry(entry);
        entry.Id = AppIdentity.ToId(entry.MergeKey);

        return entry;
    }

    [Fact]
    public void AGameAndItsLauncherShortcutBecomeOneTile()
    {
        // What HoYoPlay looks like: the library finds the game executable, the Start Menu
        // shortcut runs the launcher with the game as an argument.
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Game("Genshin Impact", @"D:\Genshin Impact\Genshin Impact game\GenshinImpact.exe"),
            Shortcut("Genshin Impact", @"C:\Program Files\HoYoPlay\launcher.exe", "--game=hk4e_global"),
        ]);

        AppEntry only = Assert.Single(merged);

        Assert.Equal(AppSource.GameLauncher, only.Source);

        // The launcher's own shortcut knows how the launcher wants it started.
        Assert.Equal(@"C:\Program Files\HoYoPlay\launcher.exe", only.TargetPath);
        Assert.Equal("--game=hk4e_global", only.Arguments);
        Assert.Equal(@"C:\Menu\Genshin Impact.lnk", only.ShortcutPath);
    }

    [Fact]
    public void TheGameKeepsItsIdentitySoTheGamesTabIsStable()
    {
        AppEntry game = Game("Rockstar Title", @"D:\Games\Title\PlayTitle.exe");
        string idAlone = Assert.Single(AppDeduplicator.Merge([Game("Rockstar Title", @"D:\Games\Title\PlayTitle.exe")])).Id;

        AppEntry merged = Assert.Single(AppDeduplicator.Merge(
            [game, Shortcut("Rockstar Title", @"C:\Rockstar\Launcher.exe", "-title")]));

        // Whether a shortcut happens to exist must not change the entry's id, or the
        // Games tab would lose track of it between scans.
        Assert.Equal(idAlone, merged.Id);
    }

    [Fact]
    public void TheGamesIconWinsOverTheLaunchersOwn()
    {
        AppEntry shortcut = Shortcut("Some Game", @"C:\Launcher\launcher.exe");
        shortcut.IconCacheFile = "launcher-icon.png";

        AppEntry merged = Assert.Single(AppDeduplicator.Merge(
            [Game("Some Game", @"D:\Games\Some Game\game.exe"), shortcut]));

        Assert.Equal("game-icon.png", merged.IconCacheFile);
    }

    [Fact]
    public void NamesAreMatchedIgnoringCaseAndSpacing()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Game("Honkai:  Star   Rail", @"D:\Games\StarRail\StarRail.exe"),
            Shortcut("honkai: star rail", @"C:\HoYoPlay\launcher.exe", "--game=hkrpg"),
        ]);

        Assert.Single(merged);
    }

    [Fact]
    public void AProtocolGameKeepsItsProtocol()
    {
        // Steam's shortcut is a .url holding the same URI, so it merges on the key alone.
        // If some other shortcut shares the name, the protocol must still win.
        AppEntry merged = Assert.Single(AppDeduplicator.Merge(
        [
            Game("Team Fortress 2", @"D:\Steam\tf2.exe", "steam://rungameid/440"),
            Shortcut("Team Fortress 2", @"C:\Steam\steam.exe", "-applaunch 440"),
        ]));

        Assert.Equal("steam://rungameid/440", merged.LaunchUri);
        Assert.Equal(LaunchKind.Uri, merged.LaunchKind);
    }

    [Fact]
    public void TwoSeparateInstallsOfTheSameTitleAreLeftAlone()
    {
        // One from itch and one from Game Jolt really are two installs.
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Game("Indie Game", @"D:\itch\indie\game.exe"),
            Game("Indie Game", @"D:\gamejolt\indie\game.exe"),
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void OrdinaryAppsThatShareANameAreLeftAlone()
    {
        // Nothing here is a game, so the name is not evidence of anything.
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Shortcut("Settings", @"C:\App One\settings.exe"),
            Shortcut("Settings", @"C:\App Two\settings.exe"),
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void APackagedAppSharingAGamesNameIsLeftAlone()
    {
        AppEntry packaged = Keyed(new AppEntry
        {
            DisplayName = "Solitaire",
            OriginalName = "Solitaire",
            Source = AppSource.Packaged,
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = "Microsoft.Solitaire_8wekyb3d8bbwe!App",
        });

        List<AppEntry> merged = AppDeduplicator.Merge(
            [Game("Solitaire", @"D:\Games\Solitaire\solitaire.exe"), packaged]);

        Assert.Equal(2, merged.Count);
    }
}
