using Launcher.Core.Discovery;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class PackagedAppIdTests
{
    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel")]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App")]
    public void Recognises_real_packaged_aumids(string aumid)
    {
        Assert.True(PackagedAppId.IsPackagedAumid(aumid));
    }

    [Theory]
    [InlineData("308046B0AF4A39CB")]                    // Firefox
    [InlineData("308046B0AF4A39CB;PrivateBrowsingAUMID")]
    [InlineData("MSEdge")]                              // Microsoft Edge
    [InlineData("VisualStudio.257105d1")]
    [InlineData("Microsoft.VisualStudio.Installer")]
    [InlineData("com.unity3d.unityhub")]
    [InlineData("Microsoft.WSL")]
    [InlineData("Brave.TNMWOHN773UQQUSU34OWSP6I74")]
    public void Rejects_legacy_win32_aumids(string aumid)
    {
        // These are real values observed on a Windows 11 machine. Desktop apps stamp them
        // on shortcuts for taskbar grouping; none are in the package catalog, so treating
        // them as packaged would discard the target path and break launching.
        Assert.False(PackagedAppId.IsPackagedAumid(aumid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!")]
    [InlineData("!App")]
    [InlineData("Family_publisher!")]
    [InlineData("NoUnderscore!App")]
    public void Rejects_malformed_input(string? aumid)
    {
        Assert.False(PackagedAppId.IsPackagedAumid(aumid));
    }
}
