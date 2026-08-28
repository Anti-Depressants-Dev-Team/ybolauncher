namespace Launcher.Core.Launching;

/// <summary>
/// Outcome of a launch attempt.
/// <para>
/// A failure is a value, not an exception: SPEC.md requires a non-blocking InfoBar rather
/// than a crash or a silent no-op, and the caller needs to tell "it broke" apart from
/// "the user clicked No on the UAC prompt".
/// </para>
/// </summary>
/// <param name="Succeeded">True when something was actually started.</param>
/// <param name="WasCancelled">True when the user dismissed the elevation prompt.</param>
/// <param name="ErrorMessage">Human-readable reason, set only when the attempt failed.</param>
public sealed record LaunchResult(bool Succeeded, bool WasCancelled, string? ErrorMessage)
{
    public static LaunchResult Success() => new(true, false, null);

    /// <summary>The user declined the UAC prompt. Not an error - say nothing to them.</summary>
    public static LaunchResult Cancelled() => new(false, true, null);

    public static LaunchResult Failed(string message) => new(false, false, message);
}
