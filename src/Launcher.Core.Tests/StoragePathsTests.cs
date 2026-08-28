using Launcher.Core;
using Launcher.Core.Storage;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class StoragePathsTests
{
    [Fact]
    public void Resolve_WithoutPortableMarker_UsesLocalAppData()
    {
        using var temp = new TempDirectory();

        StoragePaths paths = StoragePaths.Resolve(temp.Path, @"C:\Users\someone\AppData\Local");

        Assert.False(paths.IsPortable);
        Assert.Equal(
            Path.Combine(@"C:\Users\someone\AppData\Local", AppInfo.DataFolderName),
            paths.Root);
    }

    [Fact]
    public void Resolve_WithPortableMarker_UsesApplicationFolder()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(AppInfo.PortableMarkerFileName), string.Empty);

        StoragePaths paths = StoragePaths.Resolve(temp.Path, @"C:\Users\someone\AppData\Local");

        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(temp.Path, "data"), paths.Root);
    }

    [Fact]
    public void DocumentPaths_AllSitUnderRoot()
    {
        var paths = new StoragePaths(@"C:\state", isPortable: false);

        Assert.Equal(@"C:\state\settings.json", paths.SettingsFile);
        Assert.Equal(@"C:\state\apps.json", paths.AppsFile);
        Assert.Equal(@"C:\state\tabs.json", paths.TabsFile);
        Assert.Equal(@"C:\state\iconcache", paths.IconCacheDirectory);
    }

    [Fact]
    public void EnsureCreated_CreatesRootAndIconCache()
    {
        using var temp = new TempDirectory();
        var paths = new StoragePaths(Path.Combine(temp.Path, "state"), isPortable: false);

        Assert.True(paths.EnsureCreated());
        Assert.True(Directory.Exists(paths.Root));
        Assert.True(Directory.Exists(paths.IconCacheDirectory));
    }

    [Fact]
    public void IsPortableInstall_OnMissingDirectory_ReturnsFalse()
    {
        // Must not throw - the probe runs before anything else during startup.
        Assert.False(StoragePaths.IsPortableInstall(@"Z:\does\not\exist"));
    }
}
