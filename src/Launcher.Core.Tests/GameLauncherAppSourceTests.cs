using Launcher.Core.Discovery;
using Launcher.Core.Discovery.Games;
using Launcher.Core.Icons;
using Launcher.Core.Models;
using Launcher.Core.Storage;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Drives the game source over a real Steam folder layout written to disk, so the whole
/// path - library discovery, manifest parsing, entry conversion - is exercised even on a
/// machine with no launcher installed.
/// </summary>
public sealed class GameLauncherAppSourceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string BuildSteamLayout()
    {
        string steam = Path.Combine(_temp.Path, "Steam");
        string second = Path.Combine(_temp.Path, "SteamLibrary");

        Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "Portal 2"));
        Directory.CreateDirectory(Path.Combine(second, "steamapps", "common", "Team Fortress 2"));

        File.WriteAllText(
            Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "contentstatsid"  "-1"
                "0" { "path" "{{steam.Replace(@"\", @"\\")}}" }
                "1" { "path" "{{second.Replace(@"\", @"\\")}}" }
            }
            """);

        WriteManifest(steam, "620", "Portal 2", "4");
        WriteManifest(second, "440", "Team Fortress 2", "4");

        // Not a game, and not finished downloading: neither may show up.
        WriteManifest(second, "228980", "Steamworks Common Redistributables", "4");
        WriteManifest(second, "570", "Dota 2", "1026");

        return steam;
    }

    private static void WriteManifest(string library, string appId, string name, string stateFlags) =>
        File.WriteAllText(
            Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"),
            $$"""
            "AppState"
            {
                "appid"       "{{appId}}"
                "name"        "{{name}}"
                "StateFlags"  "{{stateFlags}}"
                "installdir"  "{{name}}"
            }
            """);

    [Fact]
    public async Task DiscoversInstalledGamesAcrossEveryLibraryFolder()
    {
        string steam = BuildSteamLayout();

        var source = new GameLauncherAppSource(
            [new SteamLibrary(() => steam)],
            new IconService(new StoragePaths(_temp.Path, isPortable: true)));

        IReadOnlyList<AppEntry> entries = await source.DiscoverAsync(new DiscoveryContext(32, null));

        Assert.Equal(
            ["Portal 2", "Team Fortress 2"],
            entries.Select(e => e.DisplayName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GamesLaunchThroughTheSteamProtocolButKeepTheirInstallFolder()
    {
        string steam = BuildSteamLayout();

        var source = new GameLauncherAppSource(
            [new SteamLibrary(() => steam)],
            new IconService(new StoragePaths(_temp.Path, isPortable: true)));

        IReadOnlyList<AppEntry> entries = await source.DiscoverAsync(new DiscoveryContext(32, null));
        AppEntry tf2 = entries.Single(e => e.DisplayName == "Team Fortress 2");

        Assert.Equal(AppSource.GameLauncher, tf2.Source);
        Assert.Equal(LaunchKind.Uri, tf2.LaunchKind);
        Assert.Equal("steam://rungameid/440", tf2.LaunchUri);

        // The folder is what makes "open file location" work.
        Assert.EndsWith(@"SteamLibrary\steamapps\common\Team Fortress 2", tf2.WorkingDirectory);
        Assert.False(string.IsNullOrEmpty(tf2.Id));
    }

    [Fact]
    public async Task ALauncherThatThrowsDoesNotCostTheOthers()
    {
        string steam = BuildSteamLayout();

        var source = new GameLauncherAppSource(
            [new ThrowingLibrary(), new SteamLibrary(() => steam)],
            new IconService(new StoragePaths(_temp.Path, isPortable: true)));

        IReadOnlyList<AppEntry> entries = await source.DiscoverAsync(new DiscoveryContext(32, null));

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task NoLauncherInstalledIsAnEmptyResultRatherThanAFailure()
    {
        // The normal case on a machine with no games.
        var source = new GameLauncherAppSource(
            [new SteamLibrary(() => null), new EpicLibrary(() => null)],
            new IconService(new StoragePaths(_temp.Path, isPortable: true)));

        Assert.Empty(await source.DiscoverAsync(new DiscoveryContext(32, null)));
    }

    private sealed class ThrowingLibrary : IGameLibrary
    {
        public string Name => "Broken";

        public IReadOnlyList<GameEntry> Enumerate() => throw new InvalidOperationException("boom");
    }
}
