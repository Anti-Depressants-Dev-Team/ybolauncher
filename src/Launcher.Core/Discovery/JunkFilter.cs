using System.Text;
using Launcher.Core.Models;

namespace Launcher.Core.Discovery;

/// <summary>
/// Decides whether a discovered entry is a real app or Start Menu clutter.
/// <para>
/// Rejected entries are marked, not deleted, so the "show filtered entries" setting can
/// reveal them without a rescan. Matching is on whole words: "Visual Studio Installer" is
/// a real app, "Uninstall Foo" is not, and a substring test cannot tell them apart.
/// </para>
/// </summary>
public sealed class JunkFilter
{
    /// <summary>Single words that mark an entry as clutter.</summary>
    private static readonly HashSet<string> JunkWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall", "uninstaller", "deinstall", "remove",
        "readme", "changelog", "licence", "license", "eula", "copyright",
        "documentation", "docs", "manual", "faq", "help", "tutorial", "troubleshoot",
        "website", "homepage", "webseite",
    };

    /// <summary>Two-word phrases that mark an entry as clutter.</summary>
    private static readonly HashSet<string> JunkPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "read me", "release notes", "user guide", "user manual", "getting started",
        "home page", "what's new", "whats new", "quick start", "on the web",
        "report a", "check for", "visit the", "getting help", "product documentation",
    };

    /// <summary>Target extensions that are documents or web links rather than programs.</summary>
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".rtf", ".doc", ".docx", ".pdf", ".chm", ".hlp", ".md", ".nfo", ".log",
    };

    private static readonly HashSet<string> WebExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".url", ".html", ".htm", ".mht", ".mhtml", ".website",
    };

    private readonly Func<string, bool> _fileExists;

    /// <param name="fileExists">
    /// Existence probe, injectable so broken-shortcut detection can be tested without
    /// touching the disk. The default accepts directories as well as files: a Start Menu
    /// shortcut to a folder opens Explorer, which is a real thing to launch, not a broken
    /// target.
    /// </param>
    public JunkFilter(Func<string, bool>? fileExists = null) =>
        _fileExists = fileExists ?? (path => File.Exists(path) || Directory.Exists(path));

    /// <summary>
    /// Returns <see cref="FilterReason.None"/> when the entry should be kept.
    /// </summary>
    public FilterReason Evaluate(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Packaged apps come from the package catalog, which has no clutter in it.
        // Their names ("Xbox Game Bar") can trip the word list, so trust the source.
        if (entry.LaunchKind == LaunchKind.PackagedApp)
        {
            return FilterReason.None;
        }

        if (HasJunkName(entry.OriginalName))
        {
            return IsUninstallerName(entry.OriginalName)
                ? FilterReason.Uninstaller
                : FilterReason.Documentation;
        }

        if (entry.LaunchKind == LaunchKind.Uri)
        {
            return IsWebUri(entry.LaunchUri) ? FilterReason.WebLink : FilterReason.None;
        }

        if (string.IsNullOrWhiteSpace(entry.TargetPath))
        {
            return FilterReason.NoLaunchTarget;
        }

        string extension = SafeGetExtension(entry.TargetPath);

        if (WebExtensions.Contains(extension))
        {
            return FilterReason.WebLink;
        }

        if (DocumentExtensions.Contains(extension))
        {
            return FilterReason.Documentation;
        }

        if (IsSystemComponent(entry.TargetPath))
        {
            return FilterReason.SystemComponent;
        }

        // A shortcut whose target is gone is dead weight; the app was uninstalled.
        if (!_fileExists(entry.TargetPath))
        {
            return FilterReason.BrokenTarget;
        }

        return FilterReason.None;
    }

    private static bool IsUninstallerName(string name)
    {
        foreach (string word in Tokenize(name))
        {
            if (word is "uninstall" or "uninstaller" or "deinstall" or "remove")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasJunkName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        List<string> words = Tokenize(name);

        foreach (string word in words)
        {
            if (JunkWords.Contains(word))
            {
                return true;
            }
        }

        for (int i = 0; i < words.Count - 1; i++)
        {
            if (JunkPhrases.Contains(words[i] + " " + words[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Lowercased word list. Apostrophes are kept so "what's" stays one word.</summary>
    private static List<string> Tokenize(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    private static bool IsWebUri(string? uri) =>
        uri is not null
        && (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Installer stubs and MSI repair entries live under Windows\Installer and are never
    /// something the user wants to launch from a tile.
    /// </summary>
    private static bool IsSystemComponent(string targetPath)
    {
        string normalized = targetPath.Replace('/', '\\');

        if (normalized.Contains(@"\windows\installer\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fileName = SafeGetFileName(normalized);
        return fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("installer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeGetExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string SafeGetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
