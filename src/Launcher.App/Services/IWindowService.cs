using Microsoft.UI.Xaml;

namespace Launcher.App.Services;

/// <summary>
/// Show, hide and exit the shell window, for callers that must not know about the window
/// itself - the tray menu, the global hotkey, and "hide after launching an app".
/// </summary>
public interface IWindowService
{
    /// <summary>Binds to the shell window. Called by the window during construction.</summary>
    void Attach(Window window);

    bool IsVisible { get; }

    /// <summary>Shows the window and brings it to the foreground, restoring it if minimized.</summary>
    void ShowAndActivate();

    void Hide();

    /// <summary>Hides when visible, summons when not. What the global hotkey does.</summary>
    void Toggle();

    /// <summary>
    /// Really quits, bypassing the close-to-tray setting. The only way out once the
    /// window's close button has been repurposed to hide.
    /// </summary>
    void RequestExit();

    /// <summary>True once <see cref="RequestExit"/> has been called.</summary>
    bool IsExiting { get; }
}
