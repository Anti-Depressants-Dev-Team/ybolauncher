using Launcher.Core.Interop;
using Launcher.Core.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Launcher.App.Services;

/// <inheritdoc cref="IThemeService"/>
public sealed class ThemeService : IThemeService
{
    private Window? _window;
    private FrameworkElement? _root;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public BackdropKind CurrentBackdrop { get; private set; } = BackdropKind.Mica;

    public void Attach(Window window, FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(root);

        _window = window;
        _root = root;
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        if (_root is null)
        {
            return;
        }

        _root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public void ApplyBackdrop(BackdropKind backdrop)
    {
        CurrentBackdrop = backdrop;

        if (_window is null)
        {
            return;
        }

        // Mica needs Windows 11 build 22000+. On Windows 10 the call would leave the
        // window transparent, so fall back to the solid theme brush instead.
        //
        // High contrast also opts out: a translucent, wallpaper-tinted backdrop defeats
        // the whole point of a high contrast palette.
        if (!MicaController.IsSupported() || SystemAccessibility.IsHighContrast())
        {
            _window.SystemBackdrop = null;
            return;
        }

        _window.SystemBackdrop = new MicaBackdrop
        {
            Kind = backdrop == BackdropKind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base,
        };
    }
}
