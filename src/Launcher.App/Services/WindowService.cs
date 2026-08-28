using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Launcher.App.Services;

/// <inheritdoc cref="IWindowService"/>
public sealed class WindowService : IWindowService
{
    private Window? _window;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    public bool IsExiting { get; private set; }

    public bool IsVisible => _window?.AppWindow?.IsVisible ?? false;

    public void ShowAndActivate()
    {
        if (_window?.AppWindow is not { } appWindow)
        {
            return;
        }

        appWindow.Show();

        // Show alone leaves a minimized window minimized, and does not raise it above
        // whatever the user was in when the hotkey fired.
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _window.Activate();
        BringToForeground();
    }

    public void Hide() => _window?.AppWindow?.Hide();

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowAndActivate();
        }
    }

    public void RequestExit()
    {
        IsExiting = true;
        _window?.Close();
    }

    /// <summary>
    /// Activate() raises the window within our own process, but Windows will not let a
    /// background process steal focus outright. Attaching to the foreground window's input
    /// queue is the long-standing way to make a summoned window actually take focus.
    /// </summary>
    private void BringToForeground()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            nint handle = WindowNative.GetWindowHandle(_window);

            nint foreground = NativeMethods.GetForegroundWindow();
            uint ourThread = NativeMethods.GetCurrentThreadId();
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);

            if (foregroundThread != 0 && foregroundThread != ourThread)
            {
                NativeMethods.AttachThreadInput(ourThread, foregroundThread, attach: true);
                NativeMethods.SetForegroundWindow(handle);
                NativeMethods.AttachThreadInput(ourThread, foregroundThread, attach: false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(handle);
            }
        }
        catch (Exception)
        {
            // Failing to steal focus is a cosmetic problem, never a crash.
        }
    }
}
