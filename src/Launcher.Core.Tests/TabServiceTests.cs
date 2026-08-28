using Launcher.Core.Models;
using Launcher.Core.Storage;
using Launcher.Core.Tabs;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class TabServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly StoragePaths _paths;
    private readonly JsonStorageService _storage = new();

    public TabServiceTests()
    {
        _paths = new StoragePaths(_temp.Path, isPortable: false);
    }

    private TabService NewService() => new(_storage, _paths);

    private static async Task<TabService> LoadedAsync(TabService service)
    {
        await service.LoadAsync();
        return service;
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Load_withNoFile_createsHome()
    {
        TabService tabs = await LoadedAsync(NewService());

        Assert.Single(tabs.Tabs);
        Assert.True(tabs.Home.IsHome);
        Assert.Equal(LauncherTab.HomeId, tabs.Home.Id);
    }

    [Fact]
    public async Task Home_isAlwaysFirst_evenAfterAddingTabs()
    {
        TabService tabs = await LoadedAsync(NewService());

        await tabs.CreateTabAsync("Games");
        await tabs.CreateTabAsync("Work");

        Assert.Equal(3, tabs.Tabs.Count);
        Assert.True(tabs.Tabs[0].IsHome);
    }

    [Fact]
    public async Task Home_cannotBeRenamedOrDeleted()
    {
        TabService tabs = await LoadedAsync(NewService());

        await tabs.RenameTabAsync(LauncherTab.HomeId, "Not Home");
        bool deleted = await tabs.DeleteTabAsync(LauncherTab.HomeId);

        Assert.Equal("Home", tabs.Home.Name);
        Assert.False(deleted);
        Assert.Single(tabs.Tabs);
    }

    [Fact]
    public async Task Home_cannotBeMovedAwayFromIndexZero()
    {
        TabService tabs = await LoadedAsync(NewService());
        await tabs.CreateTabAsync("Games");

        await tabs.MoveTabAsync(LauncherTab.HomeId, 1);

        Assert.True(tabs.Tabs[0].IsHome);
    }

    [Fact]
    public async Task MovingATabToIndexZero_landsAfterHomeInstead()
    {
        TabService tabs = await LoadedAsync(NewService());
        await tabs.CreateTabAsync("Games");
        LauncherTab work = await tabs.CreateTabAsync("Work");

        await tabs.MoveTabAsync(work.Id, 0);

        Assert.True(tabs.Tabs[0].IsHome);
        Assert.Equal("Work", tabs.Tabs[1].Name);
    }

    [Fact]
    public async Task Reordering_movesATabWithinTheStrip()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.CreateTabAsync("Work");

        await tabs.MoveTabAsync(games.Id, 2);

        Assert.Equal("Work", tabs.Tabs[1].Name);
        Assert.Equal("Games", tabs.Tabs[2].Name);
    }

    [Fact]
    public async Task Reorder_appliesTheGivenOrder()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        LauncherTab work = await tabs.CreateTabAsync("Work");

        await tabs.ReorderAsync([work.Id, LauncherTab.HomeId, games.Id]);

        // Home is pinned no matter where the strip tried to drop it.
        Assert.True(tabs.Tabs[0].IsHome);
        Assert.Equal("Work", tabs.Tabs[1].Name);
        Assert.Equal("Games", tabs.Tabs[2].Name);
    }

    [Fact]
    public async Task Reorder_keepsTabsTheCallerOmitted()
    {
        TabService tabs = await LoadedAsync(NewService());
        await tabs.CreateTabAsync("Games");
        LauncherTab work = await tabs.CreateTabAsync("Work");

        await tabs.ReorderAsync([work.Id]);

        // A partial list must not silently delete the rest.
        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Contains(tabs.Tabs, t => t.Name == "Games");
    }

    [Fact]
    public async Task Reorder_withUnknownIds_ignoresThem()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");

        await tabs.ReorderAsync(["nonsense", games.Id]);

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.True(tabs.Tabs[0].IsHome);
    }

    [Fact]
    public async Task DeletingATab_leavesItsAppsAlone()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.AddEntriesAsync(games.Id, ["app-a", "app-b"]);

        Assert.True(await tabs.DeleteTabAsync(games.Id));

        // The tab is gone but nothing claims to have deleted the apps; Home still shows
        // everything because Home membership is implicit.
        Assert.Single(tabs.Tabs);
        Assert.True(tabs.Contains(LauncherTab.HomeId, "app-a"));
    }

    [Fact]
    public async Task AddingToHome_isANoOp()
    {
        TabService tabs = await LoadedAsync(NewService());

        await tabs.AddEntriesAsync(LauncherTab.HomeId, ["app-a"]);

        // Home contains everything implicitly; it must not accumulate a membership list.
        Assert.Empty(tabs.Home.EntryIds);
        Assert.True(tabs.Contains(LauncherTab.HomeId, "app-a"));
    }

    [Fact]
    public async Task RemovingFromHome_isANoOp()
    {
        TabService tabs = await LoadedAsync(NewService());
        await tabs.SetOrderAsync(LauncherTab.HomeId, ["app-a", "app-b"]);

        await tabs.RemoveEntriesAsync(LauncherTab.HomeId, ["app-a"]);

        // Hiding removes an app from Home, not un-listing it.
        Assert.Equal(2, tabs.Home.EntryIds.Count);
    }

    [Fact]
    public async Task AddingAnEntryTwice_doesNotDuplicateIt()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");

        await tabs.AddEntriesAsync(games.Id, ["app-a"]);
        await tabs.AddEntriesAsync(games.Id, ["app-a", "app-b"]);

        Assert.Equal(["app-a", "app-b"], games.EntryIds);
    }

    [Fact]
    public async Task AddingAtAnIndex_insertsThere()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.AddEntriesAsync(games.Id, ["a", "b", "c"]);

        await tabs.AddEntriesAsync(games.Id, ["x"], insertIndex: 1);

        Assert.Equal(["a", "x", "b", "c"], games.EntryIds);
    }

    [Fact]
    public async Task SettingOrder_switchesTheTabToManualSort()
    {
        TabService tabs = await LoadedAsync(NewService());

        Assert.Equal(SortMode.Alphabetical, tabs.Home.SortMode);

        await tabs.SetOrderAsync(LauncherTab.HomeId, ["b", "a"]);

        // A manual drag is an explicit choice, so auto-sorting must stop or the order
        // would silently revert.
        Assert.Equal(SortMode.Manual, tabs.Home.SortMode);
        Assert.Equal(["b", "a"], tabs.Home.EntryIds);
    }

    [Fact]
    public async Task Pruning_dropsEntriesThatNoLongerExist()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.AddEntriesAsync(games.Id, ["kept", "uninstalled"]);

        bool changed = await tabs.PruneAsync(new HashSet<string>(StringComparer.Ordinal) { "kept" });

        Assert.True(changed);
        Assert.Equal(["kept"], games.EntryIds);
    }

    [Fact]
    public async Task Pruning_reportsNoChange_whenEverythingStillExists()
    {
        TabService tabs = await LoadedAsync(NewService());
        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.AddEntriesAsync(games.Id, ["kept"]);

        Assert.False(await tabs.PruneAsync(new HashSet<string>(StringComparer.Ordinal) { "kept", "other" }));
    }

    [Fact]
    public async Task TabsSurviveARoundTrip()
    {
        TabService first = await LoadedAsync(NewService());
        LauncherTab games = await first.CreateTabAsync("Games", "🎮", "#FF8800");
        await first.AddEntriesAsync(games.Id, ["app-a", "app-b"]);

        TabService second = await LoadedAsync(NewService());

        Assert.Equal(2, second.Tabs.Count);
        Assert.True(second.Tabs[0].IsHome);
        Assert.Equal("Games", second.Tabs[1].Name);
        Assert.Equal("🎮", second.Tabs[1].Glyph);
        Assert.Equal("#FF8800", second.Tabs[1].AccentColorHex);
        Assert.Equal(["app-a", "app-b"], second.Tabs[1].EntryIds);
    }

    [Fact]
    public async Task AFileWithNoHomeTab_getsOneBack()
    {
        // Simulates a hand-edited or partially written tabs.json.
        await _storage.SaveAsync(
            _paths.TabsFile,
            new TabLayout { Tabs = [new LauncherTab { Id = "games", Name = "Games" }] });

        TabService tabs = await LoadedAsync(NewService());

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.True(tabs.Tabs[0].IsHome);
        Assert.Equal("Games", tabs.Tabs[1].Name);
    }

    [Fact]
    public async Task DuplicateTabIds_areReKeyed()
    {
        await _storage.SaveAsync(
            _paths.TabsFile,
            new TabLayout
            {
                Tabs =
                [
                    LauncherTab.CreateHome(),
                    new LauncherTab { Id = "same", Name = "One" },
                    new LauncherTab { Id = "same", Name = "Two" },
                ],
            });

        TabService tabs = await LoadedAsync(NewService());

        // Two tabs sharing an id would make one of them unaddressable.
        Assert.Equal(3, tabs.Tabs.Count);
        Assert.NotEqual(tabs.Tabs[1].Id, tabs.Tabs[2].Id);
    }

    [Fact]
    public async Task AnExtraHomeTabInTheFile_isCollapsedToOne()
    {
        await _storage.SaveAsync(
            _paths.TabsFile,
            new TabLayout { Tabs = [LauncherTab.CreateHome(), LauncherTab.CreateHome()] });

        TabService tabs = await LoadedAsync(NewService());

        Assert.Single(tabs.Tabs);
    }
}
