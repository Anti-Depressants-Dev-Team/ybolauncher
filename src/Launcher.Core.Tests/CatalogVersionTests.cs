using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Launcher.Core.Storage;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Discovery rules change between releases - what counts as a duplicate, what counts as a
/// game - so a catalog written by an older build has to be rebuilt rather than shown as it
/// was. Without this an update appears to do nothing until something else triggers a scan.
/// </summary>
public sealed class CatalogVersionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonStorageService _storage = new();
    private readonly StoragePaths _paths;

    public CatalogVersionTests() => _paths = new StoragePaths(_temp.Path, isPortable: false);

    public void Dispose() => _temp.Dispose();

    private AppDiscoveryService NewService(string version) =>
        new([], _storage, _paths, new SettingsService(_storage, _paths), buildVersion: version);

    private async Task WriteCatalogAsync(string? version) =>
        await _storage.SaveAsync(
            _paths.AppsFile,
            new AppCatalog
            {
                BuiltByVersion = version,
                Entries = [new AppEntry { Id = "a", DisplayName = "App", OriginalName = "App" }],
            });

    [Fact]
    public async Task ACatalogFromThisBuildIsUsedAsItIs()
    {
        await WriteCatalogAsync("1.2.3.0");

        Assert.True(await NewService("1.2.3.0").LoadCachedAsync());
    }

    [Fact]
    public async Task ACatalogFromAnotherBuildAsksForARescan()
    {
        await WriteCatalogAsync("1.2.3.0");

        Assert.False(await NewService("1.3.0.0").LoadCachedAsync());
    }

    [Fact]
    public async Task ACatalogFromBeforeThisWasRecordedAsksForARescan()
    {
        await WriteCatalogAsync(null);

        Assert.False(await NewService("1.3.0.0").LoadCachedAsync());
    }

    [Fact]
    public async Task TheCachedEntriesAreStillShownWhileThatRescanRuns()
    {
        // An empty window would be worse than a stale one for the few seconds it takes.
        await WriteCatalogAsync("0.9.0.0");

        AppDiscoveryService service = NewService("1.0.0.0");
        await service.LoadCachedAsync();

        Assert.Single(service.Entries);
    }
}
