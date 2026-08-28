using Launcher.Core.Discovery;
using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class AppIdentityTests
{
    [Fact]
    public void Executable_key_ignores_casing_and_trailing_separators()
    {
        string a = AppIdentity.ForExecutable(@"C:\Program Files\App\App.exe", null);
        string b = AppIdentity.ForExecutable(@"c:\program files\app\app.EXE", null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Executable_key_separates_different_arguments()
    {
        // Two shortcuts to the same binary with different switches are different apps -
        // a browser profile launcher, or a game's config tool.
        string plain = AppIdentity.ForExecutable(@"C:\App\app.exe", null);
        string profile = AppIdentity.ForExecutable(@"C:\App\app.exe", "--profile work");

        Assert.NotEqual(plain, profile);
    }

    [Fact]
    public void Executable_key_normalizes_argument_whitespace()
    {
        string a = AppIdentity.ForExecutable(@"C:\App\app.exe", "--profile   work");
        string b = AppIdentity.ForExecutable(@"C:\App\app.exe", " --profile work ");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Packaged_key_ignores_aumid_casing()
    {
        Assert.Equal(
            AppIdentity.ForPackagedApp("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            AppIdentity.ForPackagedApp("microsoft.windowscalculator_8wekyb3d8bbwe!app"));
    }

    [Fact]
    public void ForEntry_prefers_aumid_over_target_path()
    {
        // This is what merges a Store app's Start Menu shortcut with its catalog entry:
        // the shortcut also has a path, but the AUMID is the stronger identity.
        var entry = new AppEntry
        {
            DisplayName = "Calculator",
            AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            TargetPath = @"C:\Windows\System32\calc.exe",
        };

        Assert.StartsWith("aumid:", AppIdentity.ForEntry(entry), StringComparison.Ordinal);
    }

    [Fact]
    public void ForEntry_falls_back_through_uri_then_path_then_name()
    {
        var uriEntry = new AppEntry { DisplayName = "Game", LaunchUri = "steam://rungameid/620" };
        Assert.StartsWith("uri:", AppIdentity.ForEntry(uriEntry), StringComparison.Ordinal);

        var pathEntry = new AppEntry { DisplayName = "App", TargetPath = @"C:\App\app.exe" };
        Assert.StartsWith("path:", AppIdentity.ForEntry(pathEntry), StringComparison.Ordinal);

        var nameOnly = new AppEntry { DisplayName = "Mystery" };
        Assert.StartsWith("name:", AppIdentity.ForEntry(nameOnly), StringComparison.Ordinal);
    }

    [Fact]
    public void Id_is_stable_and_filename_safe()
    {
        string key = AppIdentity.ForExecutable(@"C:\App\app.exe", null);

        string first = AppIdentity.ToId(key);
        string second = AppIdentity.ToId(key);

        Assert.Equal(first, second);
        Assert.Equal(16, first.Length);
        Assert.Equal(-1, first.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Different_keys_produce_different_ids()
    {
        Assert.NotEqual(
            AppIdentity.ToId(AppIdentity.ForExecutable(@"C:\a\app.exe", null)),
            AppIdentity.ToId(AppIdentity.ForExecutable(@"C:\b\app.exe", null)));
    }
}
