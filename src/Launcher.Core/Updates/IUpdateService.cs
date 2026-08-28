namespace Launcher.Core.Updates;

/// <summary>How this copy of the launcher was installed, which decides how it can update.</summary>
public enum InstallKind
{
    /// <summary>Installed by the setup .exe, so a new setup can replace it in place.</summary>
    Installed,

    /// <summary>
    /// Unzipped, or running from a build folder. Nothing can safely overwrite a folder the
    /// user arranged themselves, so the update is handed to them rather than applied.
    /// </summary>
    Portable,
}

/// <summary>A release newer than the running copy.</summary>
/// <param name="Version">Version from the tag, e.g. <c>0.4.0</c>.</param>
/// <param name="ReleaseUrl">The release page, for when the update cannot be applied here.</param>
/// <param name="AssetName">File name of the download that suits this install.</param>
/// <param name="DownloadUrl">Direct link to that file.</param>
/// <param name="SizeBytes">Size of that file, so the user knows what they are agreeing to.</param>
public sealed record UpdateInfo(
    Version Version,
    string ReleaseUrl,
    string? AssetName,
    string? DownloadUrl,
    long SizeBytes);

/// <summary>
/// Result of looking for an update. A failed check is not an error the user has to deal
/// with: no network is the normal case on plenty of machines.
/// </summary>
/// <param name="Update">The newer release, or null when there is nothing to install.</param>
/// <param name="Error">Why the check could not be made, or null when it succeeded.</param>
public sealed record UpdateCheckResult(UpdateInfo? Update, string? Error)
{
    public bool IsUpdateAvailable => Update is not null;

    public static UpdateCheckResult UpToDate { get; } = new(null, null);

    public static UpdateCheckResult Failed(string error) => new(null, error);
}

/// <summary>Finds and fetches new releases.</summary>
public interface IUpdateService
{
    /// <summary>The running version.</summary>
    Version CurrentVersion { get; }

    /// <summary>Whether this copy can replace itself.</summary>
    InstallKind InstallKind { get; }

    /// <summary>
    /// Asks the release feed for anything newer. Never throws: a check that could not be
    /// made comes back as <see cref="UpdateCheckResult.Error"/>.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update to a temporary file and returns its path, or null when the
    /// download failed.
    /// </summary>
    Task<string?> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
