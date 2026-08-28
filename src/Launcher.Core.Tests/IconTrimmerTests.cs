using System.Drawing;
using System.Drawing.Imaging;
using Launcher.Core.Icons;
using Xunit;

namespace Launcher.Core.Tests;

/// <summary>
/// Icons arrive with wildly different padding baked in - a packaged app's logo typically
/// draws inside a third of its canvas - and the tile scales whatever it is given, so the
/// padding has to come off or those apps look like small icons in an empty square.
/// </summary>
public sealed class IconTrimmerTests
{
    /// <summary>A transparent canvas with an opaque block drawn on it.</summary>
    private static Bitmap Padded(int canvas, int contentSize)
    {
        var bitmap = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);
        int offset = (canvas - contentSize) / 2;

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(Brushes.CornflowerBlue, offset, offset, contentSize, contentSize);

        return bitmap;
    }

    [Fact]
    public void CropsAPaddedIconDownToItsArtwork()
    {
        // The shape a packaged app logo actually has: a glyph in the middle third.
        using Bitmap padded = Padded(150, 50);
        using Bitmap? trimmed = IconTrimmer.Trim(padded);

        Assert.NotNull(trimmed);
        Assert.Equal(50, trimmed.Width);
        Assert.Equal(50, trimmed.Height);
    }

    [Fact]
    public void KeepsTheArtworkItself()
    {
        using Bitmap padded = Padded(120, 40);
        using Bitmap trimmed = IconTrimmer.Trim(padded)!;

        // Every pixel of the result is the drawn block, not the padding.
        Assert.Equal(Color.CornflowerBlue.ToArgb(), trimmed.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.CornflowerBlue.ToArgb(), trimmed.GetPixel(39, 39).ToArgb());
    }

    [Fact]
    public void LeavesAnIconThatAlreadyFillsItsCanvasAlone()
    {
        // A shell-extracted executable icon: no re-encoding, no quality lost.
        using Bitmap full = Padded(96, 96);

        Assert.Null(IconTrimmer.Trim(full));
    }

    [Fact]
    public void LeavesAnIconWithOnlyAThinBorderAlone()
    {
        // Not worth a re-encode, and the border may be deliberate.
        using Bitmap nearlyFull = Padded(96, 92);

        Assert.Null(IconTrimmer.Trim(nearlyFull));
    }

    [Fact]
    public void LeavesAFullyTransparentImageAlone()
    {
        using var empty = new Bitmap(64, 64, PixelFormat.Format32bppArgb);

        Assert.Null(IconTrimmer.Trim(empty));
    }

    [Fact]
    public void LeavesASpeckAlone()
    {
        // Blowing four pixels up to fill a tile would look worse than the padding does.
        using Bitmap speck = Padded(96, 4);

        Assert.Null(IconTrimmer.Trim(speck));
    }

    [Fact]
    public void FindsOffCentreArtwork()
    {
        using var bitmap = new Bitmap(100, 100, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(Brushes.Red, 10, 20, 30, 40);
        }

        Rectangle content = IconTrimmer.FindTrim(bitmap);

        Assert.Equal(new Rectangle(10, 20, 30, 40), content);
    }

    [Fact]
    public void TrimsPngBytesAndReportsNullWhenThereIsNothingToDo()
    {
        using Bitmap padded = Padded(150, 50);
        using var stream = new MemoryStream();
        padded.Save(stream, ImageFormat.Png);

        byte[]? trimmed = IconTrimmer.TrimPng(stream.ToArray());

        Assert.NotNull(trimmed);

        using var result = new Bitmap(new MemoryStream(trimmed));
        Assert.Equal(50, result.Width);

        // Trimming what is already trimmed is a no-op rather than an endless re-encode.
        Assert.Null(IconTrimmer.TrimPng(trimmed));
    }

    [Fact]
    public void UnreadableBytesAreLeftToTheCallerRatherThanThrowing()
    {
        Assert.Null(IconTrimmer.TrimPng([1, 2, 3, 4]));
        Assert.Null(IconTrimmer.TrimPng([]));
    }
}
