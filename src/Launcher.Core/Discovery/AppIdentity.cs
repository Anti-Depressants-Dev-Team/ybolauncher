using System.Security.Cryptography;
using System.Text;
using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>
/// Produces the merge key and stable id for a discovered app.
/// <para>
/// The merge key answers "are these two discovered things the same app?". Deduplication
/// groups on it, and the id is just its hash, so a rescan re-derives the same id and
/// user edits (rename, custom icon, launch counts) stay attached.
/// </para>
/// </summary>
public static class AppIdentity
{
    /// <summary>
    /// Key for a packaged app. The AUMID uniquely identifies an application within a
    /// package, so it wins over any path-based key - this is what lets a Start Menu
    /// shortcut for a Store app merge with its package catalog entry.
    /// </summary>
    public static string ForPackagedApp(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        return "aumid:" + appUserModelId.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Key for an executable. Arguments are part of the key on purpose: two shortcuts to
    /// the same binary with different switches (a browser profile, a game's config tool)
    /// are genuinely different apps.
    /// </summary>
    public static string ForExecutable(string targetPath, string? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string normalizedPath = NormalizePath(targetPath);
        string normalizedArgs = NormalizeArguments(arguments);

        return normalizedArgs.Length == 0
            ? "path:" + normalizedPath
            : "path:" + normalizedPath + "|" + normalizedArgs;
    }

    /// <summary>Key for a protocol launch such as <c>steam://rungameid/440</c>.</summary>
    public static string ForUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        return "uri:" + uri.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Last-resort key for an entry with nothing launchable, keyed on its name so at
    /// least repeated scans agree with each other.
    /// </summary>
    public static string ForName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return "name:" + displayName.Trim().ToLowerInvariant();
    }

    /// <summary>Picks the strongest available key for an entry.</summary>
    public static string ForEntry(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrWhiteSpace(entry.AppUserModelId))
        {
            return ForPackagedApp(entry.AppUserModelId);
        }

        if (!string.IsNullOrWhiteSpace(entry.LaunchUri))
        {
            return ForUri(entry.LaunchUri);
        }

        if (!string.IsNullOrWhiteSpace(entry.TargetPath))
        {
            return ForExecutable(entry.TargetPath, entry.Arguments);
        }

        return ForName(entry.DisplayName);
    }

    /// <summary>Short, filename-safe, deterministic hash of a merge key.</summary>
    public static string ToId(string mergeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mergeKey);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(mergeKey));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// Casing- and separator-insensitive path form. Environment variables are expanded so
    /// a shortcut stored as %ProgramFiles%\x matches the same file reached literally.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        string expanded = path.Trim().Trim('"');

        try
        {
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            expanded = Path.GetFullPath(expanded);
        }
        catch (Exception)
        {
            // Keep the raw string; an unparseable path still needs a consistent key.
        }

        return expanded.TrimEnd('\\').ToLowerInvariant();
    }

    /// <summary>Collapses whitespace so equivalent argument strings produce one key.</summary>
    internal static string NormalizeArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        return string.Join(' ', arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
