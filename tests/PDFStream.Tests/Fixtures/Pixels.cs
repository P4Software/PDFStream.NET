using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PDFStream.Tests.Fixtures;

/// <summary>
/// Reads rendered output back as evidence: how much ink a bitmap holds and where it is. "Printed"
/// must mean pages were really drawn, so tests check pixels rather than trusting a status.
/// </summary>
public static class Pixels
{
    /// <summary>
    /// Counts pixels darker than mid-grey. Text in the fixtures is black, so rendered text shows up
    /// here while white paper does not.
    /// </summary>
    public static int CountDark(Bitmap bitmap)
    {
        int dark = 0;
        ForEachDarkPixel(bitmap, (_, _) => dark++);
        return dark;
    }

    /// <summary>Counts the dark pixels (as <see cref="CountDark"/> defines them) inside <paramref name="area"/>.</summary>
    public static int CountDarkIn(Bitmap bitmap, Rectangle area)
    {
        int dark = 0;
        ForEachDarkPixel(bitmap, (x, y) =>
        {
            if (area.Contains(x, y))
            {
                dark++;
            }
        });
        return dark;
    }

    /// <summary>
    /// Which pixels are dark, row by row (index <c>y * width + x</c>). Two renderings of the same page can be
    /// compared with it pixel for pixel.
    /// </summary>
    public static bool[] DarkMask(Bitmap bitmap)
    {
        var mask = new bool[bitmap.Width * bitmap.Height];
        int width = bitmap.Width;
        ForEachDarkPixel(bitmap, (x, y) => mask[(y * width) + x] = true);
        return mask;
    }

    /// <summary>
    /// The smallest rectangle holding every dark pixel, or <see cref="Rectangle.Empty"/> when the
    /// bitmap has none. Its position shows where a page landed on the sheet; its shape shows whether
    /// a line of text runs across (wider than tall) or was turned (taller than wide).
    /// </summary>
    public static Rectangle InkBounds(Bitmap bitmap)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        ForEachDarkPixel(bitmap, (x, y) =>
        {
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        });

        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static void ForEachDarkPixel(Bitmap bitmap, Action<int, int> onDark)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

        // Locking as 32-bit ARGB converts 24-bit sheets too, so one reader serves both formats.
        BitmapData data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < row.Length; x++)
                {
                    int argb = row[x];
                    int red = (argb >> 16) & 0xFF;
                    int green = (argb >> 8) & 0xFF;
                    int blue = argb & 0xFF;
                    if (red + green + blue < 3 * 128)
                    {
                        onDark(x, y);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
