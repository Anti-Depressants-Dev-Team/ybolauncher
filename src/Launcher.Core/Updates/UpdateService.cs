using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Updates;

/// <summary>
/// Looks for new releases on GitHub.
/// <para>
/// The releases feed is read anonymously over HTTPS - no token, nothing sent but the
/// request - and the only thing taken from it is a version and a download link. A check
/// that fails is reported as text, never thrown: a machine with no network is ordinary,
/// not an error the user has to deal with.
/// </para>
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Anti-Depressants-Dev-Team/ybolauncher/releases/latest";

    /// <summary>GitHub refuses requests without one.</summary>
    private const string UserAgent = "YboLauncher";

    /// <summary>
    /// 1 MB. The default 80 KB means far more round trips through the socket and the file
    /// stream than a 60 MB installer needs.
    /// </summary>
    private const int DownloadBufferBytes = 1024 * 1024;

    /// <summary>A check is a small request; a download is not, and is not capped.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ILogger<UpdateService> _logger;
    private readonly Func<string> _appDirectory;

    public UpdateService(
        Version? currentVersion = null,
        HttpMessageHandler? handler = null,
        Func<string>? appDirectory = null,
        ILogger<UpdateService>? logger = null)
    {
        CurrentVersion = currentVersion ?? ReadCurrentVersion();
        _ownsHttp = handler is null;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // No client-wide timeout: it would also cap the download, and a 60 MB installer on
        // a slow line is not a hung request. The check applies its own below.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(UserAgent, CurrentVersion.ToString()));

        _appDirectory = appDirectory ?? (() => AppContext.BaseDirectory);
        _logger = logger ?? NullLogger<UpdateService>.Instance;
    }

    public Version CurrentVersion { get; }

    /// <summary>
    /// The setup .exe leaves its uninstaller beside the app, which is what distinguishes an
    /// installed copy from an unzipped one.
    /// </summary>
    public InstallKind InstallKind
    {
        get
        {
            try
            {
                return Directory.EnumerateFiles(_appDirectory(), "unins*.exe").Any()
                    ? InstallKind.Installed
                    : InstallKind.Portable;
            }
            catch (Exception)
            {
                return InstallKind.Portable;
            }
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            using HttpResponseMessage response = await _http
                .GetAsync(LatestReleaseUrl, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(
                    string.Create(CultureInfo.CurrentCulture, $"GitHub answered {(int)response.StatusCode}."));
            }

            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            return Parse(json, CurrentVersion, InstallKind);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout, not the caller giving up.
            return UpdateCheckResult.Failed("GitHub did not answer in time.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The update check could not be made.");
            return UpdateCheckResult.Failed("Could not reach GitHub.");
        }
    }

    /// <summary>
    /// Turns the release feed into a result. Separated from the request so it can be
    /// tested against real payloads.
    /// </summary>
    public static UpdateCheckResult Parse(string json, Version currentVersion, InstallKind installKind)
    {
        JsonElement root;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed("The release feed could not be read.");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return UpdateCheckResult.Failed("The release feed could not be read.");
        }

        if (ParseVersion(GetString(root, "tag_name")) is not { } version)
        {
            return UpdateCheckResult.Failed("The latest release has no version.");
        }

        if (version <= currentVersion)
        {
            return UpdateCheckResult.UpToDate;
        }

        string releaseUrl = GetString(root, "html_url")
            ?? "https://github.com/Anti-Depressants-Dev-Team/ybolauncher/releases";

        (string? name, string? url, long size) = ChooseAsset(root, installKind);

        return new UpdateCheckResult(new UpdateInfo(version, releaseUrl, name, url, size), null);
    }

    /// <summary>
    /// Picks the download that suits this install: the setup for an installed copy, the
    /// zip for an unzipped one.
    /// </summary>
    private static (string? Name, string? Url, long Size) ChooseAsset(JsonElement root, InstallKind installKind)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null, 0);
        }

        string wanted = installKind == InstallKind.Installed ? "-setup.exe" : ".zip";

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = GetString(asset, "name");

            if (name is null || !name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long size = asset.TryGetProperty("size", out JsonElement sizeValue)
                && sizeValue.ValueKind == JsonValueKind.Number
                    ? sizeValue.GetInt64()
                    : 0;

            return (name, GetString(asset, "browser_download_url"), size);
        }

        return (null, null, 0);
    }

    public async Task<string?> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
        {
            return null;
        }

        // The asset name comes off the network, so it is used for its extension only -
        // never as a path.
        string extension = Path.GetExtension(update.AssetName);
        string destination = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"YboLauncher-{update.Version}-update{extension}"));

        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength ?? update.SizeBytes;
            string temporary = destination + ".part";

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                DownloadBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[DownloadBufferBytes];
                long copied = 0;
                double lastReported = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += read;

                    if (total is not > 0)
                    {
                        continue;
                    }

                    double fraction = Math.Min(1.0, (double)copied / total.Value);

                    // Reporting every chunk means thousands of updates marshalled to the
                    // UI thread for a download this size, which slows the download itself
                    // down. A percent at a time is all anyone can read anyway.
                    if (fraction - lastReported >= 0.01 || fraction >= 1.0)
                    {
                        lastReported = fraction;
                        progress?.Report(fraction);
                    }
                }
            }

            // Only a complete download gets the real name, so a cancelled one is never
            // mistaken for an installer.
            File.Move(temporary, destination, overwrite: true);

            return destination;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The update could not be downloaded.");
            return null;
        }
    }

    /// <summary>
    /// Reads a tag such as <c>v0.4.0</c>. Any pre-release suffix is dropped, and the result
    /// is always three components so that 0.4 and 0.4.0 compare equal.
    /// </summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string trimmed = tag.Trim().TrimStart('v', 'V');
        int suffix = trimmed.IndexOfAny(['-', '+']);

        if (suffix >= 0)
        {
            trimmed = trimmed[..suffix];
        }

        return Version.TryParse(trimmed, out Version? parsed)
            ? new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0))
            : null;
    }

    private static Version ReadCurrentVersion()
    {
        try
        {
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version;

            return version is null
                ? new Version(0, 0, 0)
                : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }
        catch (Exception)
        {
            return new Version(0, 0, 0);
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
