using Launcher.Core.Models;
using Microsoft.UI.Xaml;

namespace Launcher.App.Services;

/// <summary>
/// Owns every modal interaction. Centralised because each one needs the shell window -
/// a <c>XamlRoot</c> for content dialogs, an HWND for the file picker - and scattering
/// that lookup through view models would drag window plumbing into them.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Binds the service to the shell window. Called by the window itself rather than
    /// resolved through DI, which would re-enter the container while the window is still
    /// being constructed.
    /// </summary>
    void Attach(Window window);

    /// <summary>Single-field prompt. Returns null when the user cancels.</summary>
    Task<string?> PromptForTextAsync(string title, string label, string initialValue, string acceptButtonText);

    /// <summary>
    /// Edits launch arguments and working directory together. Returns null when cancelled.
    /// </summary>
    Task<LaunchOptionsEdit?> EditLaunchOptionsAsync(AppEntry entry);

    /// <summary>Read-only details: path, target, size, last launched, launch count.</summary>
    Task ShowPropertiesAsync(AppEntry entry, string? iconPath);

    /// <summary>
    /// Picks an image, or an executable to take an icon from. Returns the chosen path,
    /// or null when cancelled.
    /// </summary>
    Task<string?> PickIconSourceAsync();

    Task<bool> ConfirmAsync(string title, string message, string acceptButtonText);
}

/// <summary>Result of the launch-options dialog.</summary>
/// <param name="Arguments">New arguments, or null to clear them.</param>
/// <param name="WorkingDirectory">New working directory, or null to clear it.</param>
public sealed record LaunchOptionsEdit(string? Arguments, string? WorkingDirectory);
