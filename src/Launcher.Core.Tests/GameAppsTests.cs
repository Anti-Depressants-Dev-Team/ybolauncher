using Launcher.Core.Discovery.Games;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Some games install like ordinary desktop software, so the Start Menu walk is the only
/// thing that finds them and cannot tell a game from a text editor.
/// </summary>
public sealed class GameAppsTests
{
    private static AppEntry Entry(string name, string? target = null) => new()
    {
        DisplayName = name,
        OriginalName = name,
        Source = AppSource.StartMenu,
        TargetPath = target,
    };

    [Theory]
    [InlineData("Prism Launcher", @"C:\Program Files\PrismLauncher\prismlauncher.exe")]
    [InlineData("Lunar Client", @"C:\Users\x\AppData\Local\Programs\lunarclient\Lunar Client.exe")]
    [InlineData("Minecraft Launcher", @"C:\XboxGames\Minecraft Launcher\MinecraftLauncher.exe")]
    public void KnownGamesAreRecognised(string name, string target)
    {
        Assert.True(GameApps.IsKnownGame(Entry(name, target)));
    }

    [Fact]
    public void TheExecutableIsEnoughWhenTheShortcutWasRenamed()
    {
        // A shortcut can be called anything; the binary keeps its name.
        Assert.True(GameApps.IsKnownGame(Entry("mc", @"C:\Games\prismlauncher.exe")));
    }

    [Fact]
    public void TheNameIsEnoughWhenThereIsNoReadableTarget()
    {
        Assert.True(GameApps.IsKnownGame(Entry("Prism Launcher")));
    }

    [Theory]
    [InlineData("Notepad", @"C:\Windows\notepad.exe")]
    [InlineData("Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe")]
    [InlineData("Launcher", @"C:\Some App\launcher.exe")]
    public void OrdinaryAppsAreLeftAlone(string name, string target)
    {
        // Guessing at what a game looks like would put a text editor in the Games tab.
        Assert.False(GameApps.IsKnownGame(Entry(name, target)));
    }

    [Fact]
    public void ADegenerateEntryIsNotAGame()
    {
        Assert.False(GameApps.IsKnownGame(new AppEntry()));
        Assert.False(GameApps.IsKnownGame(Entry("", "not|a<path")));
    }
}
