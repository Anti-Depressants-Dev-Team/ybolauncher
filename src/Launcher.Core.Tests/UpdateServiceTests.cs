using System.Net;
using System.Net.Http;
using Launcher.Core.Updates;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// The release feed decides whether the launcher offers an update, so it is driven here
/// with the payload GitHub actually returns.
/// </summary>
public sealed class UpdateServiceTests
{
    private const string ReleaseJson = """
        {
            "tag_name": "v0.9.0",
            "name": "YBO Launcher 0.9.0",
            "html_url": "https://github.com/Anti-Depressants-Dev-Team/ybolauncher/releases/tag/v0.9.0",
            "assets": [
                {
                    "name": "YboLauncher-0.9.0-setup.exe",
                    "size": 62574932,
                    "browser_download_url": "https://example.invalid/YboLauncher-0.9.0-setup.exe"
                },
                {
                    "name": "YboLauncher-0.9.0-win-x64.zip",
                    "size": 91428528,
                    "browser_download_url": "https://example.invalid/YboLauncher-0.9.0-win-x64.zip"
                }
            ]
        }
        """;

    [Fact]
    public void OffersANewerRelease()
    {
        UpdateCheckResult result = UpdateService.Parse(ReleaseJson, new Version(0, 3, 1), InstallKind.Installed);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(0, 9, 0), result.Update!.Version);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(0, 9, 0)] // the same version
    [InlineData(1, 0, 0)] // a newer local build than the feed knows about
    public void SaysNothingWhenThereIsNothingNewer(int major, int minor, int build)
    {
        UpdateCheckResult result = UpdateService.Parse(
            ReleaseJson,
            new Version(major, minor, build),
            InstallKind.Installed);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Error);
    }

    [Fact]
    public void AnInstalledCopyIsOfferedTheSetup()
    {
        UpdateInfo update = UpdateService.Parse(ReleaseJson, new Version(0, 1, 0), InstallKind.Installed).Update!;

        Assert.Equal("YboLauncher-0.9.0-setup.exe", update.AssetName);
        Assert.EndsWith("-setup.exe", update.DownloadUrl);
    }

    [Fact]
    public void AnUnzippedCopyIsOfferedTheZip()
    {
        // Nothing should overwrite a folder the user arranged themselves.
        UpdateInfo update = UpdateService.Parse(ReleaseJson, new Version(0, 1, 0), InstallKind.Portable).Update!;

        Assert.Equal("YboLauncher-0.9.0-win-x64.zip", update.AssetName);
    }

    [Fact]
    public void AReleaseWithNoMatchingAssetStillReportsTheVersion()
    {
        // Worth telling the user about even when it cannot be applied for them.
        UpdateCheckResult result = UpdateService.Parse(
            """{ "tag_name": "v2.0.0", "html_url": "https://example.invalid/r", "assets": [] }""",
            new Version(1, 0, 0),
            InstallKind.Installed);

        Assert.True(result.IsUpdateAvailable);
        Assert.Null(result.Update!.DownloadUrl);
        Assert.Equal("https://example.invalid/r", result.Update.ReleaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "nonsense" }""")]
    public void ADegenerateFeedIsAFailedCheckRatherThanACrash(string json)
    {
        UpdateCheckResult result = UpdateService.Parse(json, new Version(0, 1, 0), InstallKind.Installed);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("v0.3.1", "0.3.1")]
    [InlineData("0.3.1", "0.3.1")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    [InlineData("v0.0.0+abc1234", "0.0.0")]
    public void ReadsTheVersionOutOfATag(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void AnUnreadableTagIsNoVersion(string? tag)
    {
        Assert.Null(UpdateService.ParseVersion(tag));
    }

    [Fact]
    public void TwoAndThreeComponentVersionsCompareAsEqual()
    {
        // The tag says 0.4, the assembly says 0.4.0; that is not an update.
        UpdateCheckResult result = UpdateService.Parse(
            """{ "tag_name": "v0.4", "assets": [] }""",
            new Version(0, 4, 0),
            InstallKind.Installed);

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task AFailedRequestIsReportedRatherThanThrown()
    {
        // No network is ordinary, not an error the user has to deal with.
        using var service = new UpdateService(
            new Version(0, 1, 0),
            new StubHandler(_ => throw new HttpRequestException("no network")));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AnErrorStatusIsReportedRatherThanThrown()
    {
        using var service = new UpdateService(
            new Version(0, 1, 0),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("403", result.Error);
    }

    [Fact]
    public async Task ReadsTheFeedOverHttp()
    {
        using var service = new UpdateService(
            new Version(0, 3, 1),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ReleaseJson),
            }));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(new Version(0, 9, 0), result.Update?.Version);
    }

    [Fact]
    public void AFolderWithAnUninstallerIsAnInstalledCopy()
    {
        using var temp = new TempDirectory();

        using (var portable = new UpdateService(new Version(0, 1, 0), appDirectory: () => temp.Path))
        {
            Assert.Equal(InstallKind.Portable, portable.InstallKind);
        }

        File.WriteAllBytes(Path.Combine(temp.Path, "unins000.exe"), new byte[16]);

        using var installed = new UpdateService(new Version(0, 1, 0), appDirectory: () => temp.Path);

        Assert.Equal(InstallKind.Installed, installed.InstallKind);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
