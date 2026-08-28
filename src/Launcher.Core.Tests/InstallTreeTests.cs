using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Electron and Squirrel apps keep a versioned folder per release beside a stub, so one
/// app owns several executables at once and shows up once per shortcut.
/// </summary>
public sealed class InstallTreeTests
{
    private static string Local(string relative) =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            relative);

    [Fact]
    public void AVersionedFolderAndAStubAreOneApp()
    {
        Assert.True(InstallTree.ShareAnInstallFolder(
            Local(@"Medal\Update.exe"),
            Local(@"Medal\app-4.1.2\Medal.exe")));
    }

    [Fact]
    public void TwoVersionedFoldersAreOneApp()
    {
        Assert.True(InstallTree.ShareAnInstallFolder(
            Local(@"Moscovium\current\MoscoviumThree.exe"),
            Local(@"Moscovium\app-1.2.3\MoscoviumThree.exe")));
    }

    [Fact]
    public void AnAppUnderProgramFilesCanShareItsOwnFolder()
    {
        Assert.True(InstallTree.ShareAnInstallFolder(
            @"C:\Program Files\ShareX\ShareX.exe",
            @"C:\Program Files\ShareX\bin\ShareX.exe"));
    }

    [Theory]
    // Two unrelated apps that merely live in the same place must never look like one.
    [InlineData(@"C:\Program Files\App One\thing.exe", @"C:\Program Files\App Two\thing.exe")]
    [InlineData(@"C:\Windows\System32\thing.exe", @"C:\Windows\thing.exe")]
    [InlineData(@"C:\Games\One\game.exe", @"C:\Games\Two\game.exe")]
    [InlineData(@"C:\one.exe", @"C:\two.exe")]
    public void UnrelatedAppsInASharedPlaceAreNotOneApp(string first, string second)
    {
        Assert.False(InstallTree.ShareAnInstallFolder(first, second));
    }

    [Fact]
    public void PerUserInstallFoldersAreNotEvidenceOnTheirOwn()
    {
        // %LocalAppData%\Programs holds one folder per app, like Program Files does.
        Assert.False(InstallTree.ShareAnInstallFolder(
            Local(@"Programs\AppOne\app.exe"),
            Local(@"Programs\AppTwo\app.exe")));
    }

    [Fact]
    public void ButTwoFilesInsideOnePerUserInstallAre()
    {
        Assert.True(InstallTree.ShareAnInstallFolder(
            Local(@"Programs\Medal\Medal.exe"),
            Local(@"Programs\Medal\resources\Medal.exe")));
    }

    [Theory]
    [InlineData(@"D:\Apps\Thing\a.exe", @"C:\Apps\Thing\a.exe")]
    [InlineData(null, @"C:\Apps\Thing\a.exe")]
    [InlineData(@"C:\Apps\Thing\a.exe", "")]
    [InlineData("not a path at all", "neither is this")]
    public void DegenerateOrUnrelatedPairsAreNotOneApp(string? first, string? second)
    {
        Assert.False(InstallTree.ShareAnInstallFolder(first, second));
    }
}

/// <summary>The same rule, through the deduplicator that uses it.</summary>
public sealed class SameInstallMergeTests
{
    private static AppEntry Shortcut(string name, string target, string shortcutName)
    {
        var entry = new AppEntry
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.Executable,
            TargetPath = target,
            ShortcutPath = @"C:\Menu\" + shortcutName + ".lnk",
        };

        entry.MergeKey = AppIdentity.ForEntry(entry);
        entry.Id = AppIdentity.ToId(entry.MergeKey);

        return entry;
    }

    private static string Local(string relative) =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            relative);

    [Fact]
    public void AnElectronAppAppearsOnce()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Shortcut("Medal", Local(@"Medal\app-4.1.2\Medal.exe"), "Medal"),
            Shortcut("Medal", Local(@"Medal\Update.exe"), "Medal (2)"),
        ]);

        AppEntry only = Assert.Single(merged);

        // The stub outlives the versioned folder, so it is the one worth keeping.
        Assert.EndsWith(@"Medal\Update.exe", only.TargetPath);
    }

    [Fact]
    public void TheSurvivorKeepsWhatTheOtherKnew()
    {
        AppEntry versioned = Shortcut("Medal", Local(@"Medal\app-4.1.2\Medal.exe"), "Medal");
        versioned.IconCacheFile = "icon.png";

        AppEntry stub = Shortcut("Medal", Local(@"Medal\Update.exe"), "Medal (2)");
        stub.IconCacheFile = null;

        AppEntry only = Assert.Single(AppDeduplicator.Merge([versioned, stub]));

        Assert.Equal("icon.png", only.IconCacheFile);
    }

    [Fact]
    public void TwoAppsThatOnlyShareAnAncestorStayTwoApps()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            Shortcut("Setup", @"C:\Program Files\One\setup.exe", "a"),
            Shortcut("Setup", @"C:\Program Files\Two\setup.exe", "b"),
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void APackagedCopyAndADesktopCopyStayTwoApps()
    {
        // Installed from the Store and installed as a program really is two installs.
        var packaged = new AppEntry
        {
            DisplayName = "ShareX",
            OriginalName = "ShareX",
            Source = AppSource.Packaged,
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = "ShareX_abc!App",
        };

        packaged.MergeKey = AppIdentity.ForEntry(packaged);

        List<AppEntry> merged = AppDeduplicator.Merge(
            [packaged, Shortcut("ShareX", @"C:\Program Files\ShareX\ShareX.exe", "ShareX")]);

        Assert.Equal(2, merged.Count);
    }
}
