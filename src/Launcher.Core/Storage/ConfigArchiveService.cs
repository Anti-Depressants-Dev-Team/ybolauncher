using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Storage;

/// <inheritdoc cref="IConfigArchiveService"/>
public sealed class ConfigArchiveService : IConfigArchiveService
{
    /// <summary>Documents that make up a configuration, in the order they are written.</summary>
    private static readonly string[] DocumentNames = ["settings.json", "tabs.json", "apps.json"];

    private const string IconCacheFolder = "iconcache";

    /// <summary>
    /// Refuse absurd archives rather than filling the disk. A real configuration with a
    /// few hundred cached icons is well under a megabyte.
    /// </summary>
    private const long MaxUncompressedBytes = 256L * 1024 * 1024;

    private readonly StoragePaths _paths;
    private readonly ILogger<ConfigArchiveService> _logger;

    public ConfigArchiveService(StoragePaths paths, ILogger<ConfigArchiveService>? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullLogger<ConfigArchiveService>.Instance;
    }

    public Task<bool> ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        return Task.Run(
            () =>
            {
                try
                {
                    string? folder = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    // Build beside the target and swap in, so an interrupted export never
                    // leaves a half-written zip where the user expects their backup.
                    string temporary = destinationPath + ".tmp";

                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }

                    using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                    {
                        foreach (string name in DocumentNames)
                        {
                            string path = Path.Combine(_paths.Root, name);
                            if (File.Exists(path))
                            {
                                archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                            }
                        }

                        AddIconCache(archive, cancellationToken);
                    }

                    File.Move(temporary, destinationPath, overwrite: true);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not export the configuration to {Path}.", destinationPath);
                    return false;
                }
            },
            cancellationToken);
    }

    private void AddIconCache(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.IconCacheDirectory))
        {
            return;
        }

        foreach (string icon in Directory.EnumerateFiles(_paths.IconCacheDirectory, "*.png"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Already-compressed PNGs; storing them avoids pointless CPU for no gain.
                archive.CreateEntryFromFile(
                    icon,
                    IconCacheFolder + "/" + Path.GetFileName(icon),
                    CompressionLevel.NoCompression);
            }
            catch (Exception ex)
            {
                // One locked icon must not fail the whole export.
                _logger.LogDebug(ex, "Skipping {Icon} during export.", icon);
            }
        }
    }

    public Task<ImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return Task.Run(
            () =>
            {
                try
                {
                    if (!File.Exists(sourcePath))
                    {
                        return ImportResult.Failed("That file no longer exists.");
                    }

                    using ZipArchive archive = ZipFile.OpenRead(sourcePath);

                    if (Validate(archive) is { } problem)
                    {
                        return ImportResult.Failed(problem);
                    }

                    // Back up first: an import replaces everything, and the user needs a
                    // way back if the archive turns out to be the wrong one.
                    string backup = Path.Combine(
                        _paths.Root,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "config-backup-{0:yyyyMMdd-HHmmss}.zip",
                            DateTime.Now));

                    if (!ExportAsync(backup, cancellationToken).GetAwaiter().GetResult())
                    {
                        return ImportResult.Failed("Could not back up the current configuration, so nothing was changed.");
                    }

                    Extract(archive, cancellationToken);

                    return ImportResult.Success(backup);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidDataException)
                {
                    return ImportResult.Failed("That file is not a readable zip archive.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not import {Path}.", sourcePath);
                    return ImportResult.Failed(ex.Message);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Returns a reason to refuse the archive, or null when it looks importable.
    /// </summary>
    private static string? Validate(ZipArchive archive)
    {
        long total = 0;
        bool hasDocument = false;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            total += entry.Length;

            if (total > MaxUncompressedBytes)
            {
                return "That archive is far larger than a configuration should be.";
            }

            if (ResolveSafePath(entry.FullName) is null)
            {
                // Classic zip-slip: an entry whose path escapes the target folder.
                return "That archive contains an unsafe path and was not imported.";
            }

            if (DocumentNames.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                hasDocument = true;
            }
        }

        return hasDocument
            ? null
            : "That archive does not contain a launcher configuration.";
    }

    private void Extract(ZipArchive archive, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Root);
        Directory.CreateDirectory(_paths.IconCacheDirectory);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ResolveSafePath(entry.FullName) is not { } relative || entry.Length == 0 && relative.EndsWith('/'))
            {
                continue;
            }

            string destination = Path.Combine(_paths.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            string? folder = Path.GetDirectoryName(destination);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>
    /// Normalises an entry path and rejects anything that would escape the root - absolute
    /// paths, drive letters, or ".." segments.
    /// </summary>
    private static string? ResolveSafePath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return null;
        }

        string normalized = entryName.Replace('\\', '/');

        if (normalized.StartsWith('/') || normalized.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        foreach (string segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return null;
            }
        }

        return normalized;
    }
}
