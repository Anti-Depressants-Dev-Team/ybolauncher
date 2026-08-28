using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Launcher.Core.Interop;
using Launcher.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Core.Icons;

/// <inheritdoc cref="IIconService"/>
[SupportedOSPlatform("windows")]
public sealed class IconService : IIconService
{
    private readonly StoragePaths _paths;
    private readonly ILogger<IconService> _logger;

    public IconService(StoragePaths paths, ILogger<IconService>? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullLogger<IconService>.Instance;
    }

    public string CacheDirectory => _paths.IconCacheDirectory;

    public string? ResolveCachedPath(string? cacheFileName)
    {
        if (string.IsNullOrWhiteSpace(cacheFileName))
        {
            return null;
        }

        string full = Path.Combine(CacheDirectory, cacheFileName);
        return File.Exists(full) ? full : null;
    }

    public string? ExtractFromPath(string sourcePath, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        try
        {
            DateTime lastWrite = GetLastWriteUtc(sourcePath);
            string cacheFileName = IconCacheKey.ForFile(sourcePath, lastWrite, pixelSize);
            string destination = Path.Combine(CacheDirectory, cacheFileName);

            // Already extracted on a previous run - this is the fast path that keeps
            // startup under a second with a warm cache.
            if (File.Exists(destination))
            {
                return cacheFileName;
            }

            Directory.CreateDirectory(CacheDirectory);

            IntPtr bitmapHandle = IntPtr.Zero;
            object? factoryObject = null;

            try
            {
                Guid iid = NativeMethods.ShellItemImageFactoryIid;
                NativeMethods.SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, ref iid, out factoryObject);

                if (factoryObject is not NativeMethods.IShellItemImageFactory factory)
                {
                    return null;
                }

                int hr = factory.GetImage(
                    new NativeMethods.Size(pixelSize, pixelSize),
                    NativeMethods.SiigbfBiggerSizeOk | NativeMethods.SiigbfIconOnly,
                    out bitmapHandle);

                if (hr != 0 || bitmapHandle == IntPtr.Zero)
                {
                    _logger.LogDebug("No shell image for {Path} (hr=0x{Hr:X8}).", sourcePath, hr);
                    return null;
                }

                return SaveBitmapHandleAsPng(bitmapHandle, destination) ? cacheFileName : null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(bitmapHandle);
                }

                if (factoryObject is not null && Marshal.IsComObject(factoryObject))
                {
                    Marshal.FinalReleaseComObject(factoryObject);
                }
            }
        }
        catch (Exception ex)
        {
            // One unreadable icon must never end the scan.
            _logger.LogDebug(ex, "Icon extraction failed for {Path}.", sourcePath);
            return null;
        }
    }

    public async Task<string?> SaveEncodedImageAsync(
        string cacheKey,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            string destination = Path.Combine(CacheDirectory, cacheKey);

            if (File.Exists(destination))
            {
                return cacheKey;
            }

            // Same temp-then-swap approach as the JSON documents: a half-written PNG in
            // the cache would render as a broken tile forever.
            string temporary = destination + ".tmp";
            await File.WriteAllBytesAsync(temporary, imageBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);

            return cacheKey;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write cached icon {Key}.", cacheKey);
            return null;
        }
    }

    public Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        int removed = 0;

        try
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return Task.FromResult(0);
            }

            foreach (string file in Directory.EnumerateFiles(CacheDirectory, "*.png"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A locked file is not worth failing the whole operation over.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear the icon cache.");
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Converts a shell HBITMAP to a PNG with its alpha channel intact.
    /// <para>
    /// <c>Image.FromHbitmap</c> is not used because it discards alpha, which turns every
    /// icon's antialiased edge into a black fringe. Instead the pixels are pulled out with
    /// GetDIBits into a top-down 32bpp buffer and handed to GDI+ directly.
    /// </para>
    /// </summary>
    private bool SaveBitmapHandleAsPng(IntPtr bitmapHandle, string destination)
    {
        var description = default(NativeMethods.Bitmap);

        if (NativeMethods.GetObject(bitmapHandle, Marshal.SizeOf<NativeMethods.Bitmap>(), ref description) == 0)
        {
            return false;
        }

        int width = description.Width;
        int height = description.Height;

        if (width <= 0 || height <= 0 || width > 2048 || height > 2048)
        {
            return false;
        }

        var info = default(NativeMethods.BitmapInfo);
        info.Header.Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>();
        info.Header.Width = width;

        // Negative height requests a top-down bitmap, matching GDI+'s row order.
        info.Header.Height = -height;
        info.Header.Planes = 1;
        info.Header.BitCount = 32;
        info.Header.Compression = NativeMethods.BiRgb;

        int byteCount = width * height * 4;
        IntPtr nativeBuffer = Marshal.AllocHGlobal(byteCount);
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);

        try
        {
            if (NativeMethods.GetDIBits(
                    screenDc,
                    bitmapHandle,
                    0,
                    (uint)height,
                    nativeBuffer,
                    ref info,
                    NativeMethods.DibRgbColors) == 0)
            {
                return false;
            }

            byte[] pixels = new byte[byteCount];
            Marshal.Copy(nativeBuffer, pixels, 0, byteCount);

            // Icons sourced from a 24-bit bitmap come back with an all-zero alpha channel,
            // which would save as a fully transparent PNG. Treat that as opaque.
            if (IsFullyTransparent(pixels))
            {
                MakeOpaque(pixels);
            }

            GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var bitmap = new Bitmap(
                    width,
                    height,
                    width * 4,
                    PixelFormat.Format32bppPArgb,
                    pinned.AddrOfPinnedObject());

                bitmap.Save(destination, ImageFormat.Png);
            }
            finally
            {
                pinned.Free();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not encode icon to {Destination}.", destination);
            return false;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    private static bool IsFullyTransparent(byte[] bgraPixels)
    {
        for (int i = 3; i < bgraPixels.Length; i += 4)
        {
            if (bgraPixels[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void MakeOpaque(byte[] bgraPixels)
    {
        for (int i = 3; i < bgraPixels.Length; i += 4)
        {
            bgraPixels[i] = 0xFF;
        }
    }

    private static DateTime GetLastWriteUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }
}
