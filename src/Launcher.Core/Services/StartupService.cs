using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Launcher.Core.Services;

/// <inheritdoc cref="IStartupService"/>
[SupportedOSPlatform("windows")]
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Command line switch that tells a startup launch to stay in the tray.</summary>
    public const string MinimizedSwitch = "--minimized";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService>? logger = null) =>
        _logger = logger ?? NullLogger<StartupService>.Instance;

    /// <summary>Value name under the Run key. Matches the product name.</summary>
    private static string ValueName => AppInfo.DataFolderName;

    /// <summary>
    /// Path to the real executable.
    /// <para>
    /// <c>Process.MainModule</c> rather than <c>Assembly.Location</c>: the launcher is an
    /// apphost, so the assembly path is the managed dll, which Windows cannot start.
    /// </para>
    /// </summary>
    public static string? GetExecutablePath()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            string? path = current.MainModule?.FileName;

            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsEnabled() => ReadValue() is not null;

    public bool IsStale()
    {
        string? stored = ReadValue();
        string? expected = GetExecutablePath();

        if (stored is null || expected is null)
        {
            return false;
        }

        // Compare only the executable, so toggling "start minimized" is not mistaken for
        // a moved installation.
        return !ExtractExecutable(stored).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(bool enabled, bool startMinimized)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                _logger.LogWarning("The Run key could not be opened for writing.");
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? executable = GetExecutablePath();
            if (executable is null)
            {
                _logger.LogWarning("Could not determine the executable path; not registering startup.");
                return false;
            }

            // Always quoted: the path routinely contains spaces, and an unquoted Run value
            // is parsed as several arguments.
            string command = startMinimized
                ? string.Format(CultureInfo.InvariantCulture, "\"{0}\" {1}", executable, MinimizedSwitch)
                : string.Format(CultureInfo.InvariantCulture, "\"{0}\"", executable);

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update the Run key.");
            return false;
        }
    }

    private string? ReadValue()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the Run key.");
            return null;
        }
    }

    /// <summary>Pulls the executable back out of a stored, possibly quoted, command line.</summary>
    private static string ExtractExecutable(string command)
    {
        string trimmed = command.Trim();

        if (trimmed.StartsWith('"'))
        {
            int closing = trimmed.IndexOf('"', 1);
            if (closing > 1)
            {
                return trimmed[1..closing];
            }
        }

        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }
}
