using Launcher.Core.Launching;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Covers the guard paths. Cases that would actually start a process are deliberately not
/// exercised here - a unit test should not spawn applications on the machine running it.
/// </summary>
public sealed class LaunchServiceTests
{
    private static readonly LaunchService Service = new();

    [Fact]
    public void CanLaunchAsAdministrator_isTrue_onlyForExecutablesWithATarget()
    {
        Assert.True(Service.CanLaunchAsAdministrator(new AppEntry
        {
            LaunchKind = LaunchKind.Executable,
            TargetPath = @"C:\App\app.exe",
        }));

        Assert.False(Service.CanLaunchAsAdministrator(new AppEntry
        {
            LaunchKind = LaunchKind.Executable,
        }));
    }

    [Fact]
    public void CanLaunchAsAdministrator_isFalse_forPackagedApps()
    {
        // AppListEntry.LaunchAsync has no elevation option, so offering the menu item
        // would be a lie.
        Assert.False(Service.CanLaunchAsAdministrator(new AppEntry
        {
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = "Vendor.App_abc!App",
        }));
    }

    [Fact]
    public void CanLaunchAsAdministrator_isFalse_forLinks()
    {
        Assert.False(Service.CanLaunchAsAdministrator(new AppEntry
        {
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "steam://rungameid/620",
        }));
    }

    [Fact]
    public void CanOpenFileLocation_needsAPathOfSomeKind()
    {
        Assert.False(Service.CanOpenFileLocation(new AppEntry { LaunchKind = LaunchKind.PackagedApp }));

        Assert.True(Service.CanOpenFileLocation(new AppEntry { TargetPath = @"C:\App\app.exe" }));

        // A dead target still has a shortcut worth revealing.
        Assert.True(Service.CanOpenFileLocation(new AppEntry { ShortcutPath = @"C:\Menu\App.lnk" }));
    }

    [Theory]
    [InlineData(LaunchKind.Executable)]
    [InlineData(LaunchKind.PackagedApp)]
    [InlineData(LaunchKind.Uri)]
    public async Task LaunchAsync_withNothingToLaunch_failsWithoutThrowing(LaunchKind kind)
    {
        // SPEC.md: launch failures must surface in an InfoBar, never as a crash.
        var entry = new AppEntry { DisplayName = "Broken", LaunchKind = kind };

        LaunchResult result = await Service.LaunchAsync(entry);

        Assert.False(result.Succeeded);
        Assert.False(result.WasCancelled);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task LaunchAsync_doesNotRecordStats_whenItFails()
    {
        var entry = new AppEntry { DisplayName = "Broken", LaunchKind = LaunchKind.Executable };

        await Service.LaunchAsync(entry);

        Assert.Equal(0, entry.LaunchCount);
        Assert.Null(entry.LastLaunchedUtc);
    }

    [Fact]
    public async Task LaunchAsync_missingExecutable_reportsAReadableReason()
    {
        var entry = new AppEntry
        {
            DisplayName = "Ghost",
            LaunchKind = LaunchKind.Executable,
            TargetPath = @"Z:\does\not\exist\ghost.exe",
        };

        LaunchResult result = await Service.LaunchAsync(entry);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task OpenFileLocationAsync_withNoFileOnDisk_fails()
    {
        var entry = new AppEntry
        {
            DisplayName = "Ghost",
            TargetPath = @"Z:\does\not\exist\ghost.exe",
        };

        LaunchResult result = await Service.OpenFileLocationAsync(entry);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }
}
