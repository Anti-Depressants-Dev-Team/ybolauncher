using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Launcher.Core.Icons;

/// <summary>
/// Crops the fully transparent border off an icon.
/// <para>
/// Icons arrive with wildly different amounts of padding baked in. A shell-extracted
/// executable icon fills its canvas, while a packaged app's <c>Square150x150Logo</c>
/// typically draws its glyph inside about a third of the image and leaves the rest
/// transparent. Both end up in the same tile, scaled to the same box, so without this the
/// packaged app looks like a small icon floating in an empty square next to a desktop app
/// that fills its tile.
/// </para>
/// <para>
/// Cropping to the artwork means the tile scales the artwork itself, so every icon ends up
/// the same visual size regardless of how its author padded it.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class IconTrimmer
{
    /// <summary>Alpha at or below this is treated as empty: it is invisible on any background.</summary>
    private const byte TransparentAlpha = 8;

    /// <summary>
    /// Leave an icon alone when its artwork already covers this much of the canvas.
    /// Re-encoding for a one-pixel border costs quality for nothing.
    /// </summary>
    private const double AlreadyFullEnough = 0.92;

    /// <summary>Below this the "artwork" is a speck, and blowing it up would look worse.</summary>
    private const int SmallestUsefulEdge = 8;

    /// <summary>
    /// Returns the artwork bounds within the image, or <see cref="Rectangle.Empty"/> when
    /// there is nothing to crop - either the image is fully transparent, already fills its
    /// canvas, or is too small to be worth it.
    /// </summary>
    public static Rectangle FindTrim(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        Rectangle content = FindContentBounds(bitmap);

        if (content.IsEmpty
            || content.Width < SmallestUsefulEdge
            || content.Height < SmallestUsefulEdge)
        {
            return Rectangle.Empty;
        }

        double coverage = Math.Max(
            (double)content.Width / bitmap.Width,
            (double)content.Height / bitmap.Height);

        return coverage >= AlreadyFullEnough ? Rectangle.Empty : content;
    }

    /// <summary>
    /// A copy of the image cropped to its artwork, or null when it does not need cropping.
    /// The caller owns the returned bitmap.
    /// </summary>
    public static Bitmap? Trim(Bitmap bitmap)
    {
        Rectangle content = FindTrim(bitmap);

        if (content.IsEmpty)
        {
            return null;
        }

        // Clone rather than draw: no resampling, so the pixels are exactly the original's.
        return bitmap.Clone(content, PixelFormat.Format32bppArgb);
    }

    /// <summary>
    /// PNG bytes cropped to their artwork, or null when the image does not need cropping
    /// or cannot be read. Used for packaged app logos, which arrive already encoded.
    /// </summary>
    public static byte[]? TrimPng(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        try
        {
            using var source = new MemoryStream(imageBytes, writable: false);
            using var bitmap = new Bitmap(source);
            using Bitmap? trimmed = Trim(bitmap);

            if (trimmed is null)
            {
                return null;
            }

            using var output = new MemoryStream();
            trimmed.Save(output, ImageFormat.Png);

            return output.ToArray();
        }
        catch (Exception)
        {
            // A logo we cannot decode is written through untouched rather than lost.
            return null;
        }
    }

    /// <summary>Bounding box of every pixel that is not effectively transparent.</summary>
    private static Rectangle FindContentBounds(Bitmap bitmap)
    {
        BitmapData? data = null;

        try
        {
            data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            byte[] row = new byte[data.Stride];

            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, data.Stride);

                for (int x = 0; x < bitmap.Width; x++)
                {
                    // 32bppArgb is laid out BGRA in memory, so alpha is the fourth byte.
                    if (row[(x * 4) + 3] <= TransparentAlpha)
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    maxY = y;
                }
            }

            return maxX < 0
                ? Rectangle.Empty
                : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
        catch (Exception)
        {
            return Rectangle.Empty;
        }
        finally
        {
            if (data is not null)
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
