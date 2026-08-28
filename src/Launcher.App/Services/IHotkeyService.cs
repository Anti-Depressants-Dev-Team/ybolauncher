using Launcher.Core.Models;
using Microsoft.UI.Xaml;

namespace Launcher.App.Services;

/// <summary>Outcome of trying to register the global hotkey.</summary>
public enum HotkeyStatus
{
    /// <summary>The user has the hotkey turned off.</summary>
    Disabled,

    /// <summary>Registered and listening.</summary>
    Active,

    /// <summary>No key, or no modifier. A bare key would be swallowed system-wide.</summary>
    Invalid,

    /// <summary>Another application already owns this combination.</summary>
    AlreadyInUse,

    /// <summary>Windows refused it for some other reason.</summary>
    Failed,
}

/// <summary>
/// The system-wide summon/hide hotkey.
/// <para>
/// Works in an unpackaged WinUI 3 app: <c>RegisterHotKey</c> posts WM_HOTKEY to the
/// window's message queue, and WinUIEx's <c>WindowMessageMonitor</c> subclasses the HWND so
/// that message can be observed - WinUI itself exposes no WndProc.
/// </para>
/// </summary>
public interface IHotkeyService
{
    /// <summary>Binds to the shell window. Called by the window during construction.</summary>
    void Attach(Window window);

    HotkeyStatus Status { get; }

    /// <summary>
    /// Registers the binding, replacing any previous one. Passing <c>enabled: false</c>
    /// unregisters. Never throws - the result says what happened.
    /// </summary>
    HotkeyStatus Apply(HotkeyBinding binding, bool enabled);

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    event EventHandler? Pressed;
}
