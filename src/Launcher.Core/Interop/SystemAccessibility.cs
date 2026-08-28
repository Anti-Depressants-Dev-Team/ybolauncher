using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.UI.ViewManagement;

namespace Launcher.Core.Interop;

/// <summary>
/// Live accessibility preferences. Queried on demand rather than cached, because the user
/// can change either of these while the app is running.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class SystemAccessibility
{
    /// <summary>
    /// True when Windows is in a high contrast theme. Backdrops and decorative effects are
    /// suppressed in that case - Mica over a high contrast palette is unreadable.
    /// </summary>
    public static bool IsHighContrast()
    {
        try
        {
            var info = new NativeMethods.HighContrastInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.HighContrastInfo>(),
            };

            if (!NativeMethods.SystemParametersInfo(NativeMethods.SpiGetHighContrast, info.Size, ref info, 0))
            {
                return false;
            }

            return (info.Flags & NativeMethods.HighContrastOn) != 0;
        }
        catch (Exception)
        {
            // If we cannot tell, assume the normal case rather than degrading the UI.
            return false;
        }
    }

    /// <summary>
    /// Honours the Windows "Show animations" setting. When it is off, motion is skipped
    /// entirely rather than merely shortened.
    /// </summary>
    public static bool AreAnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
