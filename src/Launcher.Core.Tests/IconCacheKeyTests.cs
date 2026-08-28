using Launcher.Core.Icons;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class IconCacheKeyTests
{
    private static readonly DateTime Monday = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = new(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Same_inputs_produce_the_same_key()
    {
        Assert.Equal(
            IconCacheKey.ForFile(@"C:\App\app.exe", Monday, 96),
            IconCacheKey.ForFile(@"C:\App\app.exe", Monday, 96));
    }

    [Fact]
    public void Key_ignores_path_casing()
    {
        Assert.Equal(
            IconCacheKey.ForFile(@"C:\App\App.exe", Monday, 96),
            IconCacheKey.ForFile(@"c:\app\app.EXE", Monday, 96));
    }

    [Fact]
    public void A_newer_file_gets_a_new_key()
    {
        // This is the whole point of folding in the timestamp: an app update must not
        // keep serving the previous version's icon.
        Assert.NotEqual(
            IconCacheKey.ForFile(@"C:\App\app.exe", Monday, 96),
            IconCacheKey.ForFile(@"C:\App\app.exe", Tuesday, 96));
    }

    [Fact]
    public void Different_sizes_get_different_keys()
    {
        Assert.NotEqual(
            IconCacheKey.ForFile(@"C:\App\app.exe", Monday, 96),
            IconCacheKey.ForFile(@"C:\App\app.exe", Monday, 32));
    }

    [Fact]
    public void Packaged_key_changes_with_the_package_version()
    {
        Assert.NotEqual(
            IconCacheKey.ForPackagedApp("Vendor.App_abc!App", "1.0.0.0", 96),
            IconCacheKey.ForPackagedApp("Vendor.App_abc!App", "1.1.0.0", 96));
    }

    [Fact]
    public void Keys_are_valid_png_file_names()
    {
        string key = IconCacheKey.ForFile(@"C:\Program Files\A B\app name.exe", Monday, 96);

        Assert.EndsWith(".png", key, StringComparison.Ordinal);
        Assert.Equal(-1, key.IndexOfAny(Path.GetInvalidFileNameChars()));
    }
}
