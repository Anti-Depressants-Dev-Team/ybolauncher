using Launcher.Core.Models;
using Launcher.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Services;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly IStorageService _storage;
    private readonly StoragePaths _paths;
    private readonly ILogger<SettingsService> _logger;

    // Serializes writes so two rapid setting changes cannot interleave their file swaps.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsService(
        IStorageService storage,
        StoragePaths paths,
        ILogger<SettingsService>? logger = null)
    {
        _storage = storage;
        _paths = paths;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings? loaded = await _storage
            .LoadAsync<AppSettings>(_paths.SettingsFile, cancellationToken)
            .ConfigureAwait(false);

        if (loaded is null)
        {
            _logger.LogInformation("No usable settings.json; using defaults.");
            return;
        }

        Current = loaded;
        Changed?.Invoke(this, Current);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _storage
                .SaveAsync(_paths.SettingsFile, Current, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed settings write must not take the app down.
            _logger.LogError(ex, "Could not write {Path}.", _paths.SettingsFile);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        mutate(Current);
        Changed?.Invoke(this, Current);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _writeLock.Dispose();
}
