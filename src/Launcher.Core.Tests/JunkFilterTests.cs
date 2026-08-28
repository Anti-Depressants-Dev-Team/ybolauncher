using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class JunkFilterTests
{
    /// <summary>Filter that believes every target exists, isolating the name rules.</summary>
    private static JunkFilter AllTargetsExist() => new(_ => true);

    private static AppEntry Shortcut(string name, string target = @"C:\Program Files\App\app.exe") =>
        new()
        {
            DisplayName = name,
            OriginalName = name,
            Source = AppSource.StartMenu,
            LaunchKind = LaunchKind.Executable,
            TargetPath = target,
        };

    [Theory]
    [InlineData("Uninstall Foo")]
    [InlineData("Uninstall")]
    [InlineData("Foo Uninstaller")]
    [InlineData("Remove Foo")]
    public void Rejects_uninstallers(string name)
    {
        Assert.Equal(FilterReason.Uninstaller, AllTargetsExist().Evaluate(Shortcut(name)));
    }

    [Theory]
    [InlineData("Foo Documentation")]
    [InlineData("Read Me")]
    [InlineData("Readme")]
    [InlineData("Release Notes")]
    [InlineData("Foo User Guide")]
    [InlineData("Foo Help")]
    [InlineData("License")]
    [InlineData("What's New")]
    public void Rejects_documentation(string name)
    {
        Assert.Equal(FilterReason.Documentation, AllTargetsExist().Evaluate(Shortcut(name)));
    }

    [Theory]
    [InlineData("Visual Studio Installer")]
    [InlineData("Steam")]
    [InlineData("Helper Tool")]
    [InlineData("Remote Desktop Connection")]
    public void Keeps_real_apps_whose_names_merely_contain_junk_substrings(string name)
    {
        // "Installer" is not "Uninstall"; "Helper" is not "Help"; "Remote" is not "Remove".
        // A substring test would reject all of these.
        Assert.Equal(FilterReason.None, AllTargetsExist().Evaluate(Shortcut(name)));
    }

    [Theory]
    [InlineData("Foo Manual")]
    [InlineData("Foo Help")]
    public void Filters_help_and_manual_as_whole_words(string name)
    {
        // SPEC.md asks for "readme/help/manual links" to be filtered by default. That does
        // cost the occasional real app whose name ends in one of these words, which is why
        // filtered entries stay in the catalog behind the show-filtered-entries toggle
        // rather than being dropped.
        Assert.Equal(FilterReason.Documentation, AllTargetsExist().Evaluate(Shortcut(name)));
    }

    [Fact]
    public void Rejects_shortcut_whose_target_is_gone()
    {
        var filter = new JunkFilter(_ => false);
        Assert.Equal(FilterReason.BrokenTarget, filter.Evaluate(Shortcut("Ghost App")));
    }

    [Fact]
    public void Rejects_document_targets()
    {
        AppEntry entry = Shortcut("Some Notes", @"C:\Program Files\App\notes.pdf");
        Assert.Equal(FilterReason.Documentation, AllTargetsExist().Evaluate(entry));
    }

    [Fact]
    public void Rejects_internet_shortcuts()
    {
        var entry = new AppEntry
        {
            DisplayName = "Product Page",
            OriginalName = "Product Page",
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "https://example.com",
        };

        Assert.Equal(FilterReason.WebLink, AllTargetsExist().Evaluate(entry));
    }

    [Fact]
    public void Keeps_non_web_protocol_launches()
    {
        var entry = new AppEntry
        {
            DisplayName = "Portal 2",
            OriginalName = "Portal 2",
            LaunchKind = LaunchKind.Uri,
            LaunchUri = "steam://rungameid/620",
        };

        Assert.Equal(FilterReason.None, AllTargetsExist().Evaluate(entry));
    }

    [Fact]
    public void Rejects_msi_repair_stubs()
    {
        AppEntry entry = Shortcut("Foo", @"C:\Windows\Installer\{GUID}\stub.exe");
        Assert.Equal(FilterReason.SystemComponent, AllTargetsExist().Evaluate(entry));
    }

    [Fact]
    public void Rejects_entry_with_nothing_to_launch()
    {
        var entry = new AppEntry
        {
            DisplayName = "Broken",
            OriginalName = "Broken",
            LaunchKind = LaunchKind.Executable,
        };

        Assert.Equal(FilterReason.NoLaunchTarget, AllTargetsExist().Evaluate(entry));
    }

    [Fact]
    public void Never_filters_packaged_apps()
    {
        // "Xbox Game Bar" and friends would trip the word list; the package catalog is
        // trusted because it contains no clutter to begin with.
        var entry = new AppEntry
        {
            DisplayName = "Windows Help and Support",
            OriginalName = "Windows Help and Support",
            LaunchKind = LaunchKind.PackagedApp,
            AppUserModelId = "Microsoft.Help_8wekyb3d8bbwe!App",
        };

        Assert.Equal(FilterReason.None, AllTargetsExist().Evaluate(entry));
    }
}
