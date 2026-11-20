using System.Drawing;
using System.Drawing.Drawing2D;
using PDFStream.Engine;

namespace PDFStream.Printing;

/// <summary>
/// Puts one PDF page on one printer sheet. First it decides where the page goes, how large it is and
/// whether it is turned (<see cref="ComputeDestination"/>, pure layout math). Then it draws the page:
/// straight onto the printer's device context, or, when the driver refuses that, as a bitmap at the
/// printer's resolution.
/// </summary>
/// <remarks>
/// Units: page sizes are in points (1/72 inch), as PDF measures them. Printable areas and destinations
/// are in hundredths of an inch, as the Windows print system measures them. Device rectangles are in
/// printer pixels. All PDFium work goes through <see cref="IPdfDocument"/>, which holds the engine lock.
/// </remarks>
internal static class PagePainter
{
    /// <summary>
    /// The highest resolution, in dots per inch on paper, that the bitmap fallback renders at. A US
    /// Letter page is about 34 MB as a 32-bit bitmap at 300 dpi, but 135 MB at 600 dpi and 540 MB at
    /// 1200 dpi, and a 32-bit process often cannot get that much memory in one block. 300 dpi still
    /// keeps text and barcodes sharp on paper.
    /// </summary>
    internal const int MaxFallbackDpi = 300;

    /// <summary>Hundredths of an inch per point (a point is 1/72 inch).</summary>
    private const double HundredthsPerPoint = 100.0 / 72.0;

    /// <summary>
    /// How much bigger the turned page must come out before it is turned. Without this margin, rounding
    /// noise could turn a page whose two orientations fit equally well, such as a square page.
    /// </summary>
    private const double TurnGainThreshold = 1e-6;

    /// <summary>
    /// The largest device coordinate passed on. It keeps the arithmetic for a huge page printed at
    /// actual size far from integer overflow; everything beyond the sheet is cut off anyway.
    /// </summary>
    private const double MaxDeviceCoordinate = 1 << 30;

    /// <summary>
    /// Works out where a page goes on the sheet. Fit to page scales the page to the largest size that
    /// fits the printable area while keeping its proportions, and centres it. Actual size keeps the
    /// page at 100% and centres it, so a page larger than the area loses the same amount on each side.
    /// In both modes a page is turned a quarter onto the sheet (landscape onto portrait or the reverse)
    /// when that makes it larger (fit) or keeps more of it on the paper (actual size).
    /// </summary>
    /// <param name="pageSizePoints">The page size as displayed, in points (any /Rotate already applied).</param>
    /// <param name="printableArea">The part of the sheet the printer can reach, in hundredths of an inch.</param>
    /// <param name="scale">Fit to page or actual size.</param>
    /// <param name="rotate"><see langword="true"/> when the page must be drawn turned a quarter. The
    /// returned rectangle is then the turned page's footprint (its width comes from the page height).</param>
    /// <returns>The rectangle the page fills on the sheet, in hundredths of an inch. With actual size it
    /// may reach beyond the printable area.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page or the area has no positive, finite size,
    /// or <paramref name="scale"/> is not a known mode.</exception>
    internal static RectangleF ComputeDestination(SizeF pageSizePoints, RectangleF printableArea, ScaleMode scale, out bool rotate)
    {
        RequireUsableSize(pageSizePoints.Width, pageSizePoints.Height, nameof(pageSizePoints));
        RequireUsableSize(printableArea.Width, printableArea.Height, nameof(printableArea));

        // Doubles throughout: a 200-inch page at actual size is 20000 hundredths of an inch, where
        // single-precision rounding would already show in the position.
        double pageWidth = pageSizePoints.Width;
        double pageHeight = pageSizePoints.Height;
        double areaWidth = printableArea.Width;
        double areaHeight = printableArea.Height;
        double width;
        double height;

        switch (scale)
        {
            case ScaleMode.FitToPage:
                // Units cancel out here: only the proportions of the page and the area matter.
                double upright = Math.Min(areaWidth / pageWidth, areaHeight / pageHeight);
                double turned = Math.Min(areaWidth / pageHeight, areaHeight / pageWidth);
                rotate = turned > upright * (1 + TurnGainThreshold);
                double factor = rotate ? turned : upright;
                width = (rotate ? pageHeight : pageWidth) * factor;
                height = (rotate ? pageWidth : pageHeight) * factor;
                break;

            case ScaleMode.ActualSize:
                double naturalWidth = pageWidth * HundredthsPerPoint;
                double naturalHeight = pageHeight * HundredthsPerPoint;

                // Compare how much of the page lands inside the printable area each way round.
                double uprightVisible = Math.Min(naturalWidth, areaWidth) * Math.Min(naturalHeight, areaHeight);
                double turnedVisible = Math.Min(naturalHeight, areaWidth) * Math.Min(naturalWidth, areaHeight);
                rotate = turnedVisible > uprightVisible * (1 + TurnGainThreshold);
                width = rotate ? naturalHeight : naturalWidth;
                height = rotate ? naturalWidth : naturalHeight;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown scale mode.");
        }

        double x = printableArea.X + ((areaWidth - width) / 2);
        double y = printableArea.Y + ((areaHeight - height) / 2);
        return new RectangleF((float)x, (float)y, (float)width, (float)height);
    }

    /// <summary>
    /// Switches a sheet's <see cref="Graphics"/> to hundredths of an inch, the unit this class lays
    /// pages out in, and returns the printable area in that unit.
    /// </summary>
    /// <remarks>
    /// A printer Graphics already measures in hundredths of an inch (its "display" unit), but a bitmap
    /// Graphics measures in pixels; setting the unit explicitly makes both behave the same. The visible
    /// clip bounds of a printer Graphics are the printable area: the part of the sheet the printer can
    /// reach, with the origin at its top-left corner.
    /// </remarks>
    /// <param name="graphics">The sheet to prepare.</param>
    /// <returns>The printable area, in hundredths of an inch.</returns>
    internal static RectangleF PrepareSheet(Graphics graphics)
    {
        graphics.PageUnit = GraphicsUnit.Inch;
        graphics.PageScale = 0.01f;
        return graphics.VisibleClipBounds;
    }

    /// <summary>
    /// Draws one page onto a sheet, laid out by <see cref="ComputeDestination"/>. The page goes straight
    /// onto the sheet's device context, which keeps vector content sharp and spool files small. If the
    /// engine reports that the device context refused the drawing, the page is rendered as a bitmap at
    /// the printer's resolution (at most <see cref="MaxFallbackDpi"/>) and drawn as an image instead.
    /// </summary>
    /// <param name="doc">The open document.</param>
    /// <param name="pageIndex">Zero-based index of the page to draw.</param>
    /// <param name="g">The sheet, prepared with <see cref="PrepareSheet"/> (hundredths of an inch).</param>
    /// <param name="printableArea">The printable area, as <see cref="PrepareSheet"/> returned it.</param>
    /// <param name="scale">Fit to page or actual size.</param>
    /// <param name="printerDpi">The printer's resolution in dots per inch, for the bitmap fallback.</param>
    /// <exception cref="PageTooLargeException">The device context refused the drawing and the page is
    /// too large to render as a bitmap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page or the area has no usable size.</exception>
    /// <exception cref="InvalidOperationException">The engine could not draw the page either way.</exception>
    internal static void Paint(IPdfDocument doc, int pageIndex, Graphics g, RectangleF printableArea, ScaleMode scale, int printerDpi)
    {
        SizeF pageSize = doc.GetPageSizePoints(pageIndex);
        RectangleF destination = ComputeDestination(pageSize, printableArea, scale, out bool rotate);
        int quarterTurns = rotate ? QuarterTurnsOnto(printableArea) : 0;

        if (!TryPaintOnDeviceContext(doc, pageIndex, g, destination, quarterTurns))
        {
            PaintAsBitmap(doc, pageIndex, g, destination, pageSize, rotate, quarterTurns, printerDpi);
        }
    }

    /// <summary>
    /// Which way a page is turned when it is turned. This is a chosen convention, applied the same way
    /// to every page: a landscape page on a portrait sheet turns counter-clockwise (3), so its top edge
    /// runs along the left edge of the sheet; a portrait page on a landscape sheet turns clockwise (1).
    /// Either direction prints the same content; the choice only decides which edge the top faces.
    /// </summary>
    private static int QuarterTurnsOnto(RectangleF printableArea) =>
        printableArea.Width > printableArea.Height ? 1 : 3;

    /// <summary>
    /// Draws the page straight onto the device context behind <paramref name="g"/>. Returns
    /// <see langword="false"/> when the engine reports that the device context refused the drawing,
    /// so the caller can fall back to a bitmap.
    /// </summary>
    private static bool TryPaintOnDeviceContext(IPdfDocument doc, int pageIndex, Graphics g, RectangleF destination, int quarterTurns)
    {
        Rectangle pixels = ToDevicePixels(g, destination);
        IntPtr hdc = g.GetHdc();
        try
        {
            doc.RenderToDeviceContext(pageIndex, hdc, pixels, forPrinting: true, quarterTurns);
            return true;
        }
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
        {
            // The engine's documented signal that it could not draw on this device context. A disposed
            // document is a different problem that a bitmap cannot solve, so it is not caught.
            return false;
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    /// <summary>
    /// The fallback: renders the page to a bitmap that gives the printer's resolution on paper (capped
    /// at <see cref="MaxFallbackDpi"/>) and draws it into the destination.
    /// </summary>
    private static void PaintAsBitmap(IPdfDocument doc, int pageIndex, Graphics g, RectangleF destination, SizeF pageSize, bool rotate, int quarterTurns, int printerDpi)
    {
        // The engine's dpi is relative to the page's own size. To get the wanted dots per inch on paper,
        // scale it by how much the page is enlarged or shrunk on the sheet. The page's own width ends up
        // as the destination's width, or as its height when the page is turned.
        double printedWidth = rotate ? destination.Height : destination.Width;
        double enlargement = printedWidth / (pageSize.Width * HundredthsPerPoint);
        int dotsOnPaper = printerDpi > 0 ? Math.Min(printerDpi, MaxFallbackDpi) : MaxFallbackDpi;

        // At least 1: a huge page shrunk onto a small sheet can round down to 0, which the engine refuses.
        int dpi = (int)Math.Clamp(Math.Round(dotsOnPaper * enlargement), 1, int.MaxValue);

        try
        {
            using Bitmap image = doc.RenderToBitmap(pageIndex, dpi, quarterTurns);
            g.DrawImage(image, destination);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            // How a bitmap that is too large shows up: the engine refuses the size
            // (ArgumentOutOfRangeException), GDI+ refuses to create it ("Parameter is not valid", an
            // ArgumentException) or memory runs out, notably in 32-bit processes. The page index and the
            // quarter turns were already accepted by the calls above, so here these mean the page size.
            throw new PageTooLargeException(pageIndex, ex);
        }
    }

    /// <summary>
    /// Converts a rectangle from the sheet's units to device pixels, the coordinates of the device
    /// context behind the Graphics. The Graphics' own transforms do the conversion, so the unit, the
    /// resolution and any origin shift are all taken into account.
    /// </summary>
    private static Rectangle ToDevicePixels(Graphics g, RectangleF rectangle)
    {
        PointF[] corners = [new(rectangle.Left, rectangle.Top), new(rectangle.Right, rectangle.Bottom)];
        g.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, corners);

        int left = RoundToDevice(corners[0].X);
        int top = RoundToDevice(corners[0].Y);
        int right = RoundToDevice(corners[1].X);
        int bottom = RoundToDevice(corners[1].Y);

        // Never less than one pixel: the engine refuses an empty rectangle, and a page that small prints
        // as a dot either way.
        return Rectangle.FromLTRB(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1));
    }

    private static int RoundToDevice(float value) =>
        (int)Math.Clamp(Math.Round(value), -MaxDeviceCoordinate, MaxDeviceCoordinate);

    private static void RequireUsableSize(float width, float height, string parameterName)
    {
        // Written so that NaN fails too: every comparison with NaN is false.
        if (!(width > 0 && height > 0 && float.IsFinite(width) && float.IsFinite(height)))
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{width} x {height}", "A positive, finite width and height are required.");
        }
    }

    /// <summary>
    /// Thrown by <see cref="Paint"/> when a page had to go through the bitmap fallback and the bitmap is
    /// too large to create. It carries the page, so the print job can name it in its message.
    /// </summary>
    internal sealed class PageTooLargeException : Exception
    {
        /// <summary>Creates the exception for a page.</summary>
        /// <param name="pageIndex">The zero-based index of the page that was too large.</param>
        /// <param name="innerException">The allocation failure behind it.</param>
        internal PageTooLargeException(int pageIndex, Exception innerException)
            : base($"Page {pageIndex + 1} is too large to render as a bitmap at the printer's resolution.", innerException)
        {
            PageIndex = pageIndex;
        }

        /// <summary>The zero-based index of the page that was too large.</summary>
        public int PageIndex { get; }
    }
}
