using Launcher.Core.Models;
using Microsoft.UI.Xaml;

namespace Launcher.App.Services;

/// <summary>
/// Applies theme and backdrop to the shell window.
/// <para>
/// Lives in the app layer rather than Launcher.Core because it touches
/// Microsoft.UI.Xaml types directly.
/// </para>
/// </summary>
public interface IThemeService
{
    /// <summary>Theme currently requested by the user (not the resolved light/dark value).</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>Backdrop currently applied.</summary>
    BackdropKind CurrentBackdrop { get; }

    /// <summary>
    /// Binds the service to the shell window. Must be called once, before the first
    /// <see cref="ApplyTheme"/> or <see cref="ApplyBackdrop"/>.
    /// </summary>
    void Attach(Window window, FrameworkElement root);

    void ApplyTheme(AppTheme theme);

    void ApplyBackdrop(BackdropKind backdrop);
}
