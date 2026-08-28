using Launcher.Core.Models;
using Launcher.Core.Storage;
using Launcher.Core.Tabs;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class TabGlyphsTests
{
    [Fact]
    public void EveryOfferedGlyphIsASingleFluentCharacter()
    {
        Assert.NotEmpty(TabGlyphs.All);

        foreach (TabGlyph glyph in TabGlyphs.All)
        {
            Assert.True(
                TabGlyphs.IsFluentGlyph(glyph.Glyph),
                $"'{glyph.Name}' is not a single private-use character");

            Assert.False(string.IsNullOrWhiteSpace(glyph.Name));
        }
    }

    [Fact]
    public void TheGlyphSetHasNoDuplicates()
    {
        Assert.Equal(TabGlyphs.All.Count, TabGlyphs.All.Select(g => g.Glyph).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void HomeIsPartOfTheOfferedSet()
    {
        Assert.True(TabGlyphs.IsFluentGlyph(TabGlyphs.Home));
        Assert.Contains(TabGlyphs.All, g => g.Glyph == TabGlyphs.Home);
    }

    [Theory]
    [InlineData("\U0001F3E0")] // house
    [InlineData("\U0001F3AE")] // game controller
    [InlineData("A")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ab")]
    public void EmojiAndOtherTextAreNotFluentGlyphs(string? value)
    {
        // Emoji are outside the private use area, and the astral ones are surrogate pairs,
        // so both are rejected.
        Assert.False(TabGlyphs.IsFluentGlyph(value));
    }
}

public sealed class TabGlyphMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly StoragePaths _paths;
    private readonly JsonStorageService _storage = new();

    public TabGlyphMigrationTests() =>
        _paths = new StoragePaths(_temp.Path, isPortable: false);

    public void Dispose() => _temp.Dispose();

    private TabService NewService() => new(_storage, _paths);

    [Fact]
    public async Task AnEmojiOnHomeIsReplacedWithTheFluentIcon()
    {
        // Tabs written by an older build stored emoji.
        LauncherTab home = LauncherTab.CreateHome();
        home.Glyph = "\U0001F3E0";

        await _storage.SaveAsync(_paths.TabsFile, new TabLayout { Tabs = [home] });

        TabService tabs = NewService();
        await tabs.LoadAsync();

        Assert.Equal(TabGlyphs.Home, tabs.Home.Glyph);
    }

    [Fact]
    public async Task AnEmojiOnACustomTabIsDropped()
    {
        // Rendering an emoji in the icon font would show a missing-glyph box, so it goes.
        await _storage.SaveAsync(
            _paths.TabsFile,
            new TabLayout
            {
                Tabs =
                [
                    LauncherTab.CreateHome(),
                    new LauncherTab { Id = "games", Name = "Games", Glyph = "\U0001F3AE" },
                ],
            });

        TabService tabs = NewService();
        await tabs.LoadAsync();

        Assert.Null(tabs.Tabs[1].Glyph);
        Assert.Equal("Games", tabs.Tabs[1].Name);
    }

    [Fact]
    public async Task AFluentGlyphIsKept()
    {
        string kept = TabGlyphs.All.First(g => g.Name == "Games").Glyph;

        await _storage.SaveAsync(
            _paths.TabsFile,
            new TabLayout
            {
                Tabs = [LauncherTab.CreateHome(), new LauncherTab { Id = "games", Name = "Games", Glyph = kept }],
            });

        TabService tabs = NewService();
        await tabs.LoadAsync();

        Assert.Equal(kept, tabs.Tabs[1].Glyph);
    }

    [Fact]
    public async Task SettingANonFluentGlyphIsRefused()
    {
        TabService tabs = NewService();
        await tabs.LoadAsync();

        LauncherTab games = await tabs.CreateTabAsync("Games");
        await tabs.SetAppearanceAsync(games.Id, "\U0001F3AE", null);

        // Nothing may reintroduce an emoji through the appearance path either.
        Assert.Null(games.Glyph);
    }

    [Fact]
    public async Task SettingAFluentGlyphIsAccepted()
    {
        TabService tabs = NewService();
        await tabs.LoadAsync();

        LauncherTab games = await tabs.CreateTabAsync("Games");
        string glyph = TabGlyphs.All.First(g => g.Name == "Games").Glyph;

        await tabs.SetAppearanceAsync(games.Id, glyph, "#FF8800");

        Assert.Equal(glyph, games.Glyph);
        Assert.Equal("#FF8800", games.AccentColorHex);
    }
}
