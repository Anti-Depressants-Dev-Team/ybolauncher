namespace Launcher.Core.Interop;

/// <summary>
/// Runs work on a dedicated single-threaded-apartment thread.
/// <para>
/// <c>ShellLink</c> and the shell item factories are apartment-threaded COM objects.
/// Creating them from a thread pool thread (which is MTA) succeeds, but every call then
/// crosses an apartment boundary through a proxy - which, across a few hundred shortcuts,
/// is the difference between a fast scan and a visibly slow one. A scan is one long unit
/// of work, so a throwaway STA thread per scan is simpler than a persistent pump.
/// </para>
/// </summary>
public static class StaThread
{
    /// <summary>
    /// Runs <paramref name="work"/> on a fresh STA thread and completes when it returns.
    /// Exceptions propagate to the returned task.
    /// </summary>
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            completion.SetCanceled(cancellationToken);
            return completion.Task;
        }

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Launcher discovery (STA)",
        };

        // .NET initializes COM for the apartment when the thread starts.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
