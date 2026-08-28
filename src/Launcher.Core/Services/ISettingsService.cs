using Launcher.Core.Models;

namespace Launcher.Core.Services;

/// <summary>
/// Owns the single in-memory <see cref="AppSettings"/> instance and its persistence.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// The live settings. Never null - defaults are used until <see cref="LoadAsync"/>
    /// completes, and if the file is missing or unreadable.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes, on the thread that made the change.</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>Reads settings.json. Safe to call more than once.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current settings atomically.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a change and persists it. The mutation runs against the live instance,
    /// then <see cref="Changed"/> fires and the file is written.
    /// </summary>
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default);
}
