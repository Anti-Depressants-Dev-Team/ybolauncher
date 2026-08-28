using System.Globalization;
using System.Runtime.Versioning;
using Launcher.Core.Icons;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace Launcher.Core.Discovery;

/// <summary>
/// Enumerates Store / MSIX apps through the package catalog.
/// <para>
/// These are never launched by path - the AUMID is recorded so Phase 3 can start them
/// with <c>AppListEntry.LaunchAsync</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class PackagedAppSource : IAppSource
{
    private readonly IIconService _icons;
    private readonly ILogger<PackagedAppSource> _logger;

    public PackagedAppSource(IIconService icons, ILogger<PackagedAppSource>? logger = null)
    {
        _icons = icons;
        _logger = logger ?? NullLogger<PackagedAppSource>.Instance;
    }

    public AppSource Kind => AppSource.Packaged;

    public string DisplayName => "Installed apps";

    public async Task<IReadOnlyList<AppEntry>> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<Package> packages = await Task
            .Run(FindPackages, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<AppEntry>();

        for (int i = 0; i < packages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await AddEntriesForPackageAsync(packages[i], context, entries).ConfigureAwait(false);

            if (i % 25 == 0 || i == packages.Count - 1)
            {
                context.Progress?.Report(new DiscoveryProgress(DisplayName, i + 1, packages.Count));
            }
        }

        _logger.LogInformation(
            "Package catalog produced {Count} entries from {Packages} packages.",
            entries.Count,
            packages.Count);

        return entries;
    }

    private List<Package> FindPackages()
    {
        try
        {
            // An empty user SID means the current user, which needs no elevation and no
            // packageManagement capability.
            return [.. new PackageManager().FindPackagesForUser(string.Empty)];
        }
        catch (Exception ex)
        {
            // Enumerating packages can fail outright on some managed devices. Losing Store
            // apps is survivable; failing the whole scan is not.
            _logger.LogWarning(ex, "Could not enumerate packages; skipping packaged apps.");
            return [];
        }
    }

    private async Task AddEntriesForPackageAsync(
        Package package,
        DiscoveryContext context,
        List<AppEntry> entries)
    {
        IReadOnlyList<AppListEntry> appListEntries;
        string familyName;
        string version;

        try
        {
            // Frameworks, resource packages and bundles carry no launchable app.
            if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
            {
                return;
            }

            familyName = package.Id.FamilyName;
            version = FormatVersion(package);
            appListEntries = await package.GetAppListEntriesAsync();
        }
        catch (Exception ex)
        {
            // A partially-installed or corrupt package throws here. Skip just this one.
            _logger.LogDebug(ex, "Skipping a package that could not be inspected.");
            return;
        }

        foreach (AppListEntry app in appListEntries)
        {
            AppEntry? entry = await BuildEntryAsync(app, familyName, version, context).ConfigureAwait(false);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }
    }

    private async Task<AppEntry?> BuildEntryAsync(
        AppListEntry app,
        string familyName,
        string version,
        DiscoveryContext context)
    {
        try
        {
            string aumid = app.AppUserModelId;
            string name = app.DisplayInfo.DisplayName;

            if (string.IsNullOrWhiteSpace(aumid) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var entry = new AppEntry
            {
                DisplayName = name,
                OriginalName = name,
                Source = AppSource.Packaged,
                LaunchKind = LaunchKind.PackagedApp,
                AppUserModelId = aumid,
                PackageFamilyName = familyName,
            };

            entry.MergeKey = AppIdentity.ForPackagedApp(aumid);
            entry.Id = AppIdentity.ToId(entry.MergeKey);
            entry.IconCacheFile = await TrySaveLogoAsync(app, aumid, version, context.IconPixelSize)
                .ConfigureAwait(false);

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping a packaged app entry.");
            return null;
        }
    }

    /// <summary>
    /// Pulls the app's logo out of the package. It is already a PNG, so it only needs
    /// copying into the cache - no re-encoding.
    /// </summary>
    private async Task<string?> TrySaveLogoAsync(
        AppListEntry app,
        string aumid,
        string version,
        int pixelSize)
    {
        string cacheKey = IconCacheKey.ForPackagedApp(aumid, version, pixelSize);

        if (_icons.ResolveCachedPath(cacheKey) is not null)
        {
            return cacheKey;
        }

        try
        {
            RandomAccessStreamReference logo = app.DisplayInfo.GetLogo(
                new Windows.Foundation.Size(pixelSize, pixelSize));

            using IRandomAccessStreamWithContentType stream = await logo.OpenReadAsync();

            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024)
            {
                return null;
            }

            var length = (uint)stream.Size;
            using var reader = new DataReader(stream);
            await reader.LoadAsync(length);

            byte[] bytes = new byte[length];
            reader.ReadBytes(bytes);

            return await _icons.SaveEncodedImageAsync(cacheKey, bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the logo for {Aumid}.", aumid);
            return null;
        }
    }

    private static string FormatVersion(Package package)
    {
        PackageVersion v = package.Id.Version;
        return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", v.Major, v.Minor, v.Build, v.Revision);
    }
}
