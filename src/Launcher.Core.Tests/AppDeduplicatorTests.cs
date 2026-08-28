using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class AppDeduplicatorTests
{
    private static AppEntry StartMenuShortcut(
        string name,
        string target,
        string? shortcutPath = null,
        bool filtered = false) =>
        new()
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.Executable,
            TargetPath = target,
            ShortcutPath = shortcutPath,
            IsFiltered = filtered,
            FilterReason = filtered ? FilterReason.Documentation : FilterReason.None,
            MergeKey = AppIdentity.ForExecutable(target, null),
        };

    private static AppEntry PackagedApp(string name, string aumid) =>
        new()
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.Packaged,
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = aumid,
            PackageFamilyName = "Fake_8wekyb3d8bbwe",
            MergeKey = AppIdentity.ForPackagedApp(aumid),
        };

    [Fact]
    public void Collapses_the_same_shortcut_from_both_start_menu_roots()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            StartMenuShortcut("Foo", @"C:\Program Files\Foo\foo.exe", @"C:\ProgramData\...\Foo.lnk"),
            StartMenuShortcut("Foo", @"C:\Program Files\Foo\foo.exe", @"C:\Users\me\...\Foo.lnk"),
        ]);

        Assert.Single(merged);
        Assert.Equal("Foo", merged[0].DisplayName);
        Assert.NotNull(merged[0].ShortcutPath);
    }

    [Fact]
    public void Merges_a_store_shortcut_with_its_package_catalog_entry()
    {
        const string Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

        var shortcut = new AppEntry
        {
            DisplayName = "Calculator",
            OriginalName = "Calculator",
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = Aumid,
            ShortcutPath = @"C:\ProgramData\...\Calculator.lnk",
            MergeKey = AppIdentity.ForPackagedApp(Aumid),
        };

        List<AppEntry> merged = AppDeduplicator.Merge([shortcut, PackagedApp("Calculator", Aumid)]);

        Assert.Single(merged);
        Assert.Equal(LaunchKind.PackagedApp, merged[0].LaunchKind);
        Assert.Equal(Aumid, merged[0].AppUserModelId);

        // The shortcut's own path is kept for "Open file location".
        Assert.Equal(@"C:\ProgramData\...\Calculator.lnk", merged[0].ShortcutPath);
    }

    [Fact]
    public void Packaged_entry_wins_the_launch_details_regardless_of_input_order()
    {
        const string Aumid = "Vendor.App_abc!App";

        var shortcut = new AppEntry
        {
            DisplayName = "App",
            OriginalName = "App",
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = Aumid,
            MergeKey = AppIdentity.ForPackagedApp(Aumid),
        };

        List<AppEntry> packagedFirst = AppDeduplicator.Merge([PackagedApp("App", Aumid), shortcut]);
        List<AppEntry> shortcutFirst = AppDeduplicator.Merge([shortcut, PackagedApp("App", Aumid)]);

        Assert.Equal(AppSource.Packaged, packagedFirst[0].Source);
        Assert.Equal(AppSource.Packaged, shortcutFirst[0].Source);
        Assert.Equal(packagedFirst[0].Id, shortcutFirst[0].Id);
    }

    [Fact]
    public void One_unfiltered_shortcut_redeems_the_whole_group()
    {
        // "Foo" and "Foo Documentation" can point at the same binary. The app should
        // survive because at least one route to it is legitimate.
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            StartMenuShortcut("Foo Documentation", @"C:\Foo\foo.exe", filtered: true),
            StartMenuShortcut("Foo", @"C:\Foo\foo.exe"),
        ]);

        Assert.Single(merged);
        Assert.False(merged[0].IsFiltered);
        Assert.Equal(FilterReason.None, merged[0].FilterReason);
    }

    [Fact]
    public void Group_stays_filtered_when_every_member_is_junk()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            StartMenuShortcut("Foo Help", @"C:\Foo\help.exe", filtered: true),
            StartMenuShortcut("Foo Manual", @"C:\Foo\help.exe", filtered: true),
        ]);

        Assert.Single(merged);
        Assert.True(merged[0].IsFiltered);
    }

    [Fact]
    public void Name_choice_is_deterministic_and_prefers_the_most_common()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            StartMenuShortcut("Foo 2024 (64-bit)", @"C:\Foo\foo.exe"),
            StartMenuShortcut("Foo", @"C:\Foo\foo.exe"),
            StartMenuShortcut("Foo", @"C:\Foo\foo.exe"),
        ]);

        Assert.Single(merged);
        Assert.Equal("Foo", merged[0].DisplayName);
    }

    [Fact]
    public void Distinct_apps_are_left_alone_and_keep_first_seen_order()
    {
        List<AppEntry> merged = AppDeduplicator.Merge(
        [
            StartMenuShortcut("Bravo", @"C:\B\b.exe"),
            StartMenuShortcut("Alpha", @"C:\A\a.exe"),
        ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Bravo", merged[0].DisplayName);
        Assert.Equal("Alpha", merged[1].DisplayName);
    }

    [Fact]
    public void Every_merged_entry_gets_an_id_derived_from_its_merge_key()
    {
        List<AppEntry> merged = AppDeduplicator.Merge([StartMenuShortcut("Foo", @"C:\Foo\foo.exe")]);

        Assert.Equal(AppIdentity.ToId(merged[0].MergeKey), merged[0].Id);
    }
}
