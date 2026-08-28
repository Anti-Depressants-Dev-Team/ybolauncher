using Launcher.Core.Models;
using Launcher.Core.Storage;
using Launcher.Core.Tabs;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// The Games tab appears by itself when a scan finds games, and is an ordinary tab after
/// that - including being deletable for good.
/// </summary>
public sealed class GamesTabTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonStorageService _storage = new();
    private readonly StoragePaths _paths;

    public GamesTabTests() => _paths = new StoragePaths(_temp.Path, isPortable: false);

    public void Dispose() => _temp.Dispose();

    private async Task<TabService> LoadedAsync()
    {
        var service = new TabService(_storage, _paths);
        await service.LoadAsync();

        return service;
    }

    [Fact]
    public async Task AppearsWhenTheFirstGamesAreFound()
    {
        TabService tabs = await LoadedAsync();

        Assert.True(await tabs.SyncGamesTabAsync(["game-a", "game-b"]));

        LauncherTab games = tabs.Tabs.Single(t => t.Id == LauncherTab.GamesId);

        Assert.Equal("Games", games.Name);
        Assert.Equal(TabGlyphs.Games, games.Glyph);
        Assert.Equal(["game-a", "game-b"], games.EntryIds);
    }

    [Fact]
    public async Task IsNotCreatedOnAMachineWithNoGames()
    {
        TabService tabs = await LoadedAsync();

        Assert.False(await tabs.SyncGamesTabAsync([]));
        Assert.DoesNotContain(tabs.Tabs, t => t.Id == LauncherTab.GamesId);
    }

    [Fact]
    public async Task PicksUpANewlyInstalledGame()
    {
        TabService tabs = await LoadedAsync();
        await tabs.SyncGamesTabAsync(["game-a"]);

        Assert.True(await tabs.SyncGamesTabAsync(["game-a", "game-b"]));
        Assert.Equal(["game-a", "game-b"], tabs.Tabs.Single(t => t.Id == LauncherTab.GamesId).EntryIds);
    }

    [Fact]
    public async Task AScanThatFindsNothingNewChangesNothing()
    {
        TabService tabs = await LoadedAsync();
        await tabs.SyncGamesTabAsync(["game-a"]);

        Assert.False(await tabs.SyncGamesTabAsync(["game-a"]));
    }

    [Fact]
    public async Task AGameTakenOutOfTheTabStaysOut()
    {
        TabService tabs = await LoadedAsync();
        await tabs.SyncGamesTabAsync(["game-a", "game-b"]);
        await tabs.RemoveEntriesAsync(LauncherTab.GamesId, ["game-b"]);

        await tabs.SyncGamesTabAsync(["game-a", "game-b"]);

        // Putting it back on every scan would make the tab impossible to curate.
        Assert.Equal(["game-a"], tabs.Tabs.Single(t => t.Id == LauncherTab.GamesId).EntryIds);
    }

    [Fact]
    public async Task DeletingTheTabKeepsItDeleted()
    {
        TabService tabs = await LoadedAsync();
        await tabs.SyncGamesTabAsync(["game-a"]);

        Assert.True(await tabs.DeleteTabAsync(LauncherTab.GamesId));
        Assert.False(await tabs.SyncGamesTabAsync(["game-a", "game-b"]));
        Assert.DoesNotContain(tabs.Tabs, t => t.Id == LauncherTab.GamesId);
    }

    [Fact]
    public async Task TheDeletionSurvivesARestart()
    {
        TabService first = await LoadedAsync();
        await first.SyncGamesTabAsync(["game-a"]);
        await first.DeleteTabAsync(LauncherTab.GamesId);

        TabService second = await LoadedAsync();

        Assert.False(await second.SyncGamesTabAsync(["game-a"]));
        Assert.DoesNotContain(second.Tabs, t => t.Id == LauncherTab.GamesId);
    }

    [Fact]
    public async Task WhatTheTabHasAlreadyOfferedSurvivesARestart()
    {
        TabService first = await LoadedAsync();
        await first.SyncGamesTabAsync(["game-a", "game-b"]);
        await first.RemoveEntriesAsync(LauncherTab.GamesId, ["game-b"]);

        TabService second = await LoadedAsync();
        await second.SyncGamesTabAsync(["game-a", "game-b"]);

        Assert.Equal(["game-a"], second.Tabs.Single(t => t.Id == LauncherTab.GamesId).EntryIds);
    }

    [Fact]
    public async Task IsAnOrdinaryTabOnceItExists()
    {
        TabService tabs = await LoadedAsync();
        await tabs.SyncGamesTabAsync(["game-a"]);

        await tabs.RenameTabAsync(LauncherTab.GamesId, "My Games");
        await tabs.AddEntriesAsync(LauncherTab.GamesId, ["some-app"]);

        LauncherTab games = tabs.Tabs.Single(t => t.Id == LauncherTab.GamesId);

        Assert.Equal("My Games", games.Name);
        Assert.Contains("some-app", games.EntryIds);

        // Renaming it does not turn it back into a fresh tab on the next scan.
        Assert.False(await tabs.SyncGamesTabAsync(["game-a"]));
        Assert.Equal("My Games", tabs.Tabs.Single(t => t.Id == LauncherTab.GamesId).Name);
    }

    [Fact]
    public async Task HomeStaysFirstWhenTheGamesTabArrives()
    {
        TabService tabs = await LoadedAsync();
        await tabs.CreateTabAsync("Work");
        await tabs.SyncGamesTabAsync(["game-a"]);

        Assert.True(tabs.Tabs[0].IsHome);
        Assert.Equal(LauncherTab.GamesId, tabs.Tabs[^1].Id);
    }
}
