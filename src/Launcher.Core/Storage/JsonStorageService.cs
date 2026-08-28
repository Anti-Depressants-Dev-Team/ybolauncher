using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Storage;

/// <summary>
/// JSON-backed <see cref="IStorageService"/> with atomic writes and schema migration.
/// </summary>
public sealed class JsonStorageService : IStorageService
{
    /// <summary>Property name the schema version is written under (camelCase policy).</summary>
    internal const string SchemaVersionPropertyName = "schemaVersion";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // Enums persist as names so the files stay readable and survive reordering.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IReadOnlyList<IDocumentMigration> _migrations;
    private readonly ILogger<JsonStorageService> _logger;

    public JsonStorageService(
        IEnumerable<IDocumentMigration>? migrations = null,
        ILogger<JsonStorageService>? logger = null)
    {
        _migrations = migrations?.ToArray() ?? [];
        _logger = logger ?? NullLogger<JsonStorageService>.Instance;
    }

    /// <summary>
    /// The schema version <typeparamref name="T"/> currently expects, taken from
    /// <see cref="SchemaVersionAttribute"/>. Types without the attribute are treated as v1.
    /// </summary>
    public static int CurrentVersionOf<T>() => CurrentVersionOf(typeof(T));

    internal static int CurrentVersionOf(Type type) =>
        type.GetCustomAttribute<SchemaVersionAttribute>()?.Version ?? 1;

    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}; falling back to defaults.", path);
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "{Path} is not valid JSON; quarantining it.", path);
            Quarantine(path);
            return null;
        }

        if (root is null)
        {
            _logger.LogWarning("{Path} did not contain a JSON object; quarantining it.", path);
            Quarantine(path);
            return null;
        }

        int targetVersion = CurrentVersionOf<T>();
        int fileVersion = ReadSchemaVersion(root);

        if (fileVersion > targetVersion)
        {
            // Written by a newer build. Refuse rather than silently downgrading the user's data.
            _logger.LogWarning(
                "{Path} is schema v{FileVersion} but this build understands v{TargetVersion}; ignoring it.",
                path,
                fileVersion,
                targetVersion);
            return null;
        }

        if (fileVersion < targetVersion)
        {
            root = TryMigrate<T>(root, fileVersion, targetVersion, path);
            if (root is null)
            {
                return null;
            }
        }

        try
        {
            T? result = root.Deserialize<T>(SerializerOptions);
            if (result is IVersionedDocument versioned)
            {
                versioned.SchemaVersion = targetVersion;
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "{Path} could not be deserialized to {Type}; quarantining it.", path, typeof(T).Name);
            Quarantine(path);
            return null;
        }
    }

    public async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);

        if (value is IVersionedDocument versioned)
        {
            versioned.SchemaVersion = CurrentVersionOf<T>();
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Temp file must sit on the same volume as the target for File.Replace to work.
        string tempPath = path + ".tmp";

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous))
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            // Force the bytes to the device before the swap, so a power loss cannot leave
            // us having replaced a good file with an empty one.
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    /// <summary>Reads the schema version, tolerating a missing or non-numeric property.</summary>
    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue(SchemaVersionPropertyName, out JsonNode? node) || node is null)
        {
            return 0;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Walks the migration chain from <paramref name="fromVersion"/> up to
    /// <paramref name="toVersion"/>. Returns null when the chain has a gap.
    /// </summary>
    private JsonObject? TryMigrate<T>(JsonObject root, int fromVersion, int toVersion, string path)
    {
        int version = fromVersion;

        while (version < toVersion)
        {
            IDocumentMigration? migration = _migrations.FirstOrDefault(
                m => m.DocumentType == typeof(T) && m.FromVersion == version);

            if (migration is null)
            {
                _logger.LogWarning(
                    "No migration from schema v{Version} for {Type} ({Path}); ignoring the file.",
                    version,
                    typeof(T).Name,
                    path);
                return null;
            }

            try
            {
                root = migration.Migrate(root);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Migration v{From}->v{To} for {Type} failed.", migration.FromVersion, migration.ToVersion, typeof(T).Name);
                return null;
            }

            version = migration.ToVersion;
        }

        root[SchemaVersionPropertyName] = toVersion;
        return root;
    }

    /// <summary>
    /// Moves an unreadable file aside so the user can recover it manually, instead of
    /// deleting it or overwriting it on the next save.
    /// </summary>
    private void Quarantine(string path)
    {
        try
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not quarantine {Path}.", path);
        }
    }
}
