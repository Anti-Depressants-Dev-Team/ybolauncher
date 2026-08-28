using System.Text.Json.Nodes;
using Launcher.Core.Models;
using Launcher.Core.Storage;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class JsonStorageServiceTests
{
    [SchemaVersion(2)]
    private sealed class SampleDocument : IVersionedDocument
    {
        public int SchemaVersion { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>v1 stored the display name under "title"; v2 renamed it to "name".</summary>
    private sealed class RenameTitleToName : IDocumentMigration
    {
        public Type DocumentType => typeof(SampleDocument);

        public int FromVersion => 1;

        public int ToVersion => 2;

        public JsonObject Migrate(JsonObject document)
        {
            if (document.TryGetPropertyValue("title", out JsonNode? title))
            {
                document.Remove("title");
                document["name"] = title?.GetValue<string>();
            }

            return document;
        }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();

        Assert.Null(await storage.LoadAsync<SampleDocument>(temp.File("nope.json")));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsValues()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");

        await storage.SaveAsync(path, new SampleDocument { Name = "Steam", Count = 7 });
        SampleDocument? loaded = await storage.LoadAsync<SampleDocument>(path);

        Assert.NotNull(loaded);
        Assert.Equal("Steam", loaded.Name);
        Assert.Equal(7, loaded.Count);
    }

    [Fact]
    public async Task SaveAsync_StampsCurrentSchemaVersion_AndLeavesNoTempFile()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");

        var document = new SampleDocument { Name = "Steam" };
        await storage.SaveAsync(path, document);

        Assert.Equal(2, document.SchemaVersion);
        Assert.False(File.Exists(path + ".tmp"), "the temp file must be swapped into place, not left behind");

        JsonObject? onDisk = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject;
        Assert.NotNull(onDisk);
        Assert.Equal(2, onDisk["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task SaveAsync_OverExistingFile_ReplacesItInPlace()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");

        await storage.SaveAsync(path, new SampleDocument { Name = "first", Count = 1 });
        await storage.SaveAsync(path, new SampleDocument { Name = "second", Count = 2 });

        SampleDocument? loaded = await storage.LoadAsync<SampleDocument>(path);
        Assert.NotNull(loaded);
        Assert.Equal("second", loaded.Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_QuarantinesFileAndReturnsNull()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        Assert.Null(await storage.LoadAsync<SampleDocument>(path));

        Assert.False(File.Exists(path));
        Assert.NotEmpty(Directory.GetFiles(temp.Path, "doc.json.corrupt-*"));
    }

    [Fact]
    public async Task LoadAsync_NewerSchemaVersion_ReturnsNullAndLeavesFileAlone()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 99, "name": "future" }""");

        Assert.Null(await storage.LoadAsync<SampleDocument>(path));

        // Data written by a newer build must survive untouched.
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "doc.json.corrupt-*"));
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaVersion_RunsRegisteredMigration()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService([new RenameTitleToName()]);
        string path = temp.File("doc.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 1, "title": "Legacy Name", "count": 3 }""");

        SampleDocument? loaded = await storage.LoadAsync<SampleDocument>(path);

        Assert.NotNull(loaded);
        Assert.Equal("Legacy Name", loaded.Name);
        Assert.Equal(3, loaded.Count);
        Assert.Equal(2, loaded.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaVersionWithNoMigration_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 1, "title": "Legacy Name" }""");

        Assert.Null(await storage.LoadAsync<SampleDocument>(path));
    }

    [Fact]
    public async Task LoadAsync_UnversionedFile_IsTreatedAsVersionZero()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("doc.json");
        await File.WriteAllTextAsync(path, """{ "name": "no version here" }""");

        // Version 0 cannot reach version 2 without migrations, so defaults win.
        Assert.Null(await storage.LoadAsync<SampleDocument>(path));
    }

    [Fact]
    public async Task AppSettings_RoundTrip_PersistsEnumsAsReadableNames()
    {
        using var temp = new TempDirectory();
        var storage = new JsonStorageService();
        string path = temp.File("settings.json");

        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            Backdrop = BackdropKind.MicaAlt,
            LastActiveTabId = "games",
            Window = new WindowPlacement { Width = 1280, Height = 800, Left = 40, Top = 60 },
        };

        await storage.SaveAsync(path, settings);

        string raw = await File.ReadAllTextAsync(path);
        Assert.Contains("dark", raw, StringComparison.Ordinal);
        Assert.Contains("micaAlt", raw, StringComparison.Ordinal);

        // Computed properties must stay out of the file; they would read as stored state.
        Assert.DoesNotContain("hasValue", raw, StringComparison.OrdinalIgnoreCase);

        AppSettings? loaded = await storage.LoadAsync<AppSettings>(path);
        Assert.NotNull(loaded);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(BackdropKind.MicaAlt, loaded.Backdrop);
        Assert.Equal("games", loaded.LastActiveTabId);
        Assert.Equal(1280, loaded.Window.Width);
        Assert.True(loaded.Window.HasValue);
    }

    [Fact]
    public void AppSettings_Clone_DoesNotShareWindowPlacement()
    {
        var original = new AppSettings { Window = new WindowPlacement { Width = 100 } };
        AppSettings copy = original.Clone();

        copy.Window.Width = 999;

        Assert.Equal(100, original.Window.Width);
    }
}
