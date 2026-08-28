using System.IO.Compression;
using System.Text;
using Launcher.Core.Storage;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class ConfigArchiveServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly StoragePaths _paths;
    private readonly ConfigArchiveService _archive;

    public ConfigArchiveServiceTests()
    {
        _paths = new StoragePaths(Path.Combine(_temp.Path, "state"), isPortable: false);
        _paths.EnsureCreated();
        _archive = new ConfigArchiveService(_paths);
    }

    public void Dispose() => _temp.Dispose();

    private void WriteConfig(string settings = "{\"schemaVersion\":1}")
    {
        File.WriteAllText(_paths.SettingsFile, settings);
        File.WriteAllText(_paths.TabsFile, "{\"schemaVersion\":1,\"tabs\":[]}");
        File.WriteAllText(_paths.AppsFile, "{\"schemaVersion\":1,\"entries\":[]}");
        File.WriteAllBytes(Path.Combine(_paths.IconCacheDirectory, "abc.png"), [1, 2, 3]);
    }

    private string ZipPath => Path.Combine(_temp.Path, "export.zip");

    [Fact]
    public async Task Export_writesEveryDocumentAndTheIconCache()
    {
        WriteConfig();

        Assert.True(await _archive.ExportAsync(ZipPath));

        using ZipArchive zip = ZipFile.OpenRead(ZipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("settings.json", names);
        Assert.Contains("tabs.json", names);
        Assert.Contains("apps.json", names);
        Assert.Contains("iconcache/abc.png", names);
    }

    [Fact]
    public async Task Export_leavesNoTemporaryFileBehind()
    {
        WriteConfig();

        await _archive.ExportAsync(ZipPath);

        Assert.False(File.Exists(ZipPath + ".tmp"));
    }

    [Fact]
    public async Task Export_overExistingFile_replacesIt()
    {
        WriteConfig();
        await File.WriteAllTextAsync(ZipPath, "not a zip");

        Assert.True(await _archive.ExportAsync(ZipPath));

        using ZipArchive zip = ZipFile.OpenRead(ZipPath);
        Assert.NotEmpty(zip.Entries);
    }

    [Fact]
    public async Task RoundTrip_restoresTheDocuments()
    {
        WriteConfig("{\"schemaVersion\":1,\"theme\":\"dark\"}");
        await _archive.ExportAsync(ZipPath);

        // Simulate a different machine: wipe everything, then import.
        File.Delete(_paths.SettingsFile);
        File.Delete(_paths.TabsFile);
        Directory.Delete(_paths.IconCacheDirectory, recursive: true);

        ImportResult result = await _archive.ImportAsync(ZipPath);

        Assert.True(result.Succeeded);
        Assert.Contains("\"theme\":\"dark\"", await File.ReadAllTextAsync(_paths.SettingsFile), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_paths.IconCacheDirectory, "abc.png")));
    }

    [Fact]
    public async Task Import_backsUpWhatWasThere()
    {
        WriteConfig("{\"schemaVersion\":1,\"theme\":\"light\"}");
        await _archive.ExportAsync(ZipPath);

        await File.WriteAllTextAsync(_paths.SettingsFile, "{\"schemaVersion\":1,\"theme\":\"dark\"}");

        ImportResult result = await _archive.ImportAsync(ZipPath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        // The backup must hold the configuration that was replaced, not the imported one.
        using ZipArchive backup = ZipFile.OpenRead(result.BackupPath);
        using var reader = new StreamReader(backup.GetEntry("settings.json")!.Open());
        Assert.Contains("\"theme\":\"dark\"", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_ofAMissingFile_fails()
    {
        ImportResult result = await _archive.ImportAsync(Path.Combine(_temp.Path, "nope.zip"));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Import_ofSomethingThatIsNotAZip_fails()
    {
        string path = Path.Combine(_temp.Path, "bogus.zip");
        await File.WriteAllTextAsync(path, "definitely not a zip");

        ImportResult result = await _archive.ImportAsync(path);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Import_ofAZipWithoutAConfiguration_isRefused()
    {
        string path = Path.Combine(_temp.Path, "unrelated.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("holiday.jpg").Open());
            await writer.WriteAsync("not a launcher configuration");
        }

        ImportResult result = await _archive.ImportAsync(path);

        Assert.False(result.Succeeded);
        Assert.Contains("configuration", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escaped.json")]
    [InlineData("iconcache/../../escaped.json")]
    [InlineData("/absolute.json")]
    [InlineData("C:/windows/system32/evil.json")]
    public async Task Import_refusesEntriesThatEscapeTheConfigFolder(string entryName)
    {
        // Zip slip: an archive whose entry paths write outside the destination.
        string path = Path.Combine(_temp.Path, "evil.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()))
            {
                await writer.WriteAsync("{}");
            }

            using var evil = new StreamWriter(zip.CreateEntry(entryName).Open());
            await evil.WriteAsync("pwned");
        }

        ImportResult result = await _archive.ImportAsync(path);

        Assert.False(result.Succeeded);
        Assert.Contains("unsafe", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "escaped.json")));
    }

    [Fact]
    public async Task ARefusedImport_changesNothing()
    {
        WriteConfig("{\"schemaVersion\":1,\"theme\":\"light\"}");

        string path = Path.Combine(_temp.Path, "unrelated.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("holiday.jpg");
        }

        await _archive.ImportAsync(path);

        // Not even a backup should have been written for an archive we refused.
        Assert.Contains("\"theme\":\"light\"", await File.ReadAllTextAsync(_paths.SettingsFile), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_paths.Root, "config-backup-*.zip"));
    }

    [Fact]
    public async Task Export_ofAnEmptyConfiguration_stillProducesAReadableZip()
    {
        // First run, before anything has been saved.
        Assert.True(await _archive.ExportAsync(ZipPath));

        using ZipArchive zip = ZipFile.OpenRead(ZipPath);
        Assert.Empty(zip.Entries);
    }

    [Fact]
    public async Task ImportedIconCache_replacesTheOldOne()
    {
        WriteConfig();
        await _archive.ExportAsync(ZipPath);

        // A stale icon left over from the previous configuration.
        await File.WriteAllBytesAsync(
            Path.Combine(_paths.IconCacheDirectory, "abc.png"),
            Encoding.UTF8.GetBytes("stale"));

        await _archive.ImportAsync(ZipPath);

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(_paths.IconCacheDirectory, "abc.png")));
    }
}
