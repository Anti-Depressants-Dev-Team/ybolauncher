using System.Runtime.InteropServices;
using Launcher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using WinUIEx.Messaging;

namespace Launcher.App.Services;

/// <inheritdoc cref="IHotkeyService"/>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    /// <summary>Arbitrary but stable id; we only ever register one hotkey.</summary>
    private const int HotkeyId = 0xB0B0;

    private readonly ILogger<HotkeyService> _logger;

    private WindowMessageMonitor? _monitor;
    private nint _windowHandle;
    private bool _registered;

    public HotkeyService(ILogger<HotkeyService>? logger = null) =>
        _logger = logger ?? NullLogger<HotkeyService>.Instance;

    public HotkeyStatus Status { get; private set; } = HotkeyStatus.Disabled;

    public event EventHandler? Pressed;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _windowHandle = WindowNative.GetWindowHandle(window);

        // WinUI exposes no WndProc, so the HWND is subclassed to observe WM_HOTKEY.
        _monitor = new WindowMessageMonitor(window);
        _monitor.WindowMessageReceived += OnWindowMessage;
    }

    private void OnWindowMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == NativeMethods.WmHotkey && (int)e.Message.WParam == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    public HotkeyStatus Apply(HotkeyBinding binding, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(binding);

        Unregister();

        if (!enabled)
        {
            return Status = HotkeyStatus.Disabled;
        }

        if (!binding.IsValid)
        {
            return Status = HotkeyStatus.Invalid;
        }

        if (_windowHandle == 0)
        {
            return Status = HotkeyStatus.Failed;
        }

        if (NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, binding.ToModifierFlags(), binding.Key))
        {
            _registered = true;
            return Status = HotkeyStatus.Active;
        }

        int error = Marshal.GetLastWin32Error();

        _logger.LogInformation(
            "RegisterHotKey failed for {Binding} with error {Error}.",
            binding,
            error);

        // The common failure by far: something else already owns the combination.
        return Status = error == NativeMethods.ErrorHotkeyAlreadyRegistered
            ? HotkeyStatus.AlreadyInUse
            : HotkeyStatus.Failed;
    }

    private void Unregister()
    {
        if (!_registered || _windowHandle == 0)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();

        if (_monitor is not null)
        {
            _monitor.WindowMessageReceived -= OnWindowMessage;
            _monitor.Dispose();
            _monitor = null;
        }
    }
}
