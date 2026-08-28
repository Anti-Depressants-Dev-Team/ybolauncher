using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Interop;

/// <summary>
/// Reads <c>.lnk</c> files through <c>IShellLink</c>.
/// <para>
/// Must be called on an STA thread - see <see cref="StaThread"/>.
/// </para>
/// </summary>
public sealed class ShellLinkResolver(ILogger<ShellLinkResolver>? logger = null)
{
    private readonly ILogger<ShellLinkResolver> _logger = logger ?? NullLogger<ShellLinkResolver>.Instance;

    /// <summary>
    /// Resolves one shortcut. Returns null when the file is not a readable shortcut.
    /// Never throws: a single malformed .lnk must not end the scan.
    /// </summary>
    public ShortcutTarget? Resolve(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        object? comObject = null;

        try
        {
            Type? shellLinkType = Type.GetTypeFromCLSID(NativeMethods.ShellLinkClsid);
            if (shellLinkType is null)
            {
                return null;
            }

            comObject = Activator.CreateInstance(shellLinkType);
            if (comObject is not NativeMethods.IShellLinkW link
                || comObject is not NativeMethods.IPersistFile persistFile)
            {
                return null;
            }

            persistFile.Load(shortcutPath, NativeMethods.StgmRead);

            // Deliberately NOT calling link.Resolve(): the shell's "find the moved target"
            // search can hit the network or trigger an MSI repair, costing seconds per
            // shortcut. A shortcut whose target is missing is junk anyway, and the filter
            // catches it.
            var buffer = new StringBuilder(NativeMethods.MaxPath);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            string targetPath = Expand(buffer.ToString());

            buffer.Clear();
            buffer.EnsureCapacity(NativeMethods.MaxPath * 4);
            link.GetArguments(buffer, buffer.Capacity);
            string arguments = buffer.ToString();

            buffer.Clear();
            buffer.EnsureCapacity(NativeMethods.MaxPath);
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            string workingDirectory = Expand(buffer.ToString());

            buffer.Clear();
            buffer.EnsureCapacity(NativeMethods.MaxPath);
            link.GetIconLocation(buffer, buffer.Capacity, out int iconIndex);
            string iconLocation = Expand(buffer.ToString());

            string? appUserModelId = TryReadAppUserModelId(comObject);

            return new ShortcutTarget(
                targetPath,
                NullIfEmpty(arguments),
                NullIfEmpty(workingDirectory),
                NullIfEmpty(iconLocation),
                iconIndex,
                NullIfEmpty(appUserModelId));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve shortcut {Path}.", shortcutPath);
            return null;
        }
        finally
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
    }

    /// <summary>
    /// Reads PKEY_AppUserModel_ID from the shortcut's property store. Store app shortcuts
    /// carry no target path, only this.
    /// </summary>
    private string? TryReadAppUserModelId(object comObject)
    {
        if (comObject is not NativeMethods.IPropertyStore store)
        {
            return null;
        }

        NativeMethods.PropertyKey key = NativeMethods.AppUserModelIdKey;
        NativeMethods.PropVariant value = default;

        try
        {
            store.GetValue(ref key, out value);

            if (value.VariantType == NativeMethods.VtLpwstr && value.Pointer != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(value.Pointer);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Shortcut has no readable AppUserModel.ID.");
            return null;
        }
        finally
        {
            // Frees the string the shell allocated. Nothing useful to do if it fails.
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    /// <summary>Expands %VARIABLES%, tolerating a malformed value.</summary>
    private static string Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return Environment.ExpandEnvironmentVariables(value.Trim());
        }
        catch (Exception)
        {
            return value.Trim();
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
