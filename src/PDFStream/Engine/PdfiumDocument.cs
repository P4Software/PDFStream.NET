using System.Drawing;
using System.Drawing.Imaging;

namespace PDFStream.Engine;

/// <summary>
/// A PDF opened by PDFium. Every native call is made while holding
/// <see cref="PdfiumLibrary.Sync"/>, so the document can be used from several threads.
/// </summary>
internal sealed class PdfiumDocument : IPdfDocument
{
    private readonly PdfiumDocumentHandle _handle;

    /// <summary>
    /// Wraps an open handle. The caller must hold <see cref="PdfiumLibrary.Sync"/>, because the page
    /// count is read from PDFium here, once, instead of on every access.
    /// </summary>
    /// <param name="handle">The open document; this object takes ownership of it.</param>
    internal PdfiumDocument(PdfiumDocumentHandle handle)
    {
        _handle = handle;
        PageCount = PdfiumNative.FPDF_GetPageCount(handle.DangerousGetHandle());
    }

    /// <inheritdoc/>
    public int PageCount { get; }

    /// <inheritdoc/>
    public SizeF GetPageSizePoints(int pageIndex)
    {
        ValidatePageIndex(pageIndex);

        lock (PdfiumLibrary.Sync)
        {
            return UseHandle(document =>
            {
                if (PdfiumNative.FPDF_GetPageSizeByIndexF(document, pageIndex, out PdfiumNative.FsSizeF size) == 0)
                {
                    throw new InvalidOperationException($"PDFium could not read the size of page {pageIndex + 1}.");
                }

                return new SizeF(size.Width, size.Height);
            });
        }
    }

    /// <inheritdoc/>
    public void RenderToDeviceContext(int pageIndex, IntPtr hdc, Rectangle destinationPixels, bool forPrinting, int quarterTurns)
    {
        ValidatePageIndex(pageIndex);
        ValidateQuarterTurns(quarterTurns);
        if (hdc == IntPtr.Zero)
        {
            throw new ArgumentException("A device context is required.", nameof(hdc));
        }

        if (destinationPixels.Width <= 0 || destinationPixels.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationPixels), destinationPixels, "The destination rectangle must have a positive width and height.");
        }

        int flags = PdfiumNative.RenderAnnotations | (forPrinting ? PdfiumNative.RenderForPrinting : 0);

        lock (PdfiumLibrary.Sync)
        {
            WithPage(pageIndex, page =>
            {
                // PDFium adds the page's own /Rotate to this rotation itself, so quarterTurns is
                // relative to the page as it is displayed (0 draws it exactly as a viewer shows it).
                int rendered = PdfiumNative.FPDF_RenderPage(
                    hdc,
                    page,
                    destinationPixels.X,
                    destinationPixels.Y,
                    destinationPixels.Width,
                    destinationPixels.Height,
                    quarterTurns,
                    flags);
                if (rendered == 0)
                {
                    throw new InvalidOperationException($"PDFium could not draw page {pageIndex + 1} on the device context.");
                }
            });
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The page is rendered with the printing flag. In PDFStream a page bitmap is only ever the
    /// fallback for a printer that refuses device-context rendering (or a check of printed
    /// output), so it must look exactly like the device-context path with
    /// <c>forPrinting: true</c>.
    /// </remarks>
    public Bitmap RenderToBitmap(int pageIndex, int dpi, int quarterTurns)
    {
        ValidatePageIndex(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ValidateQuarterTurns(quarterTurns);

        // A quarter turn either way lays the page on its side, so the bitmap swaps width and height.
        SizeF sizePoints = GetPageSizePoints(pageIndex);
        bool sideways = quarterTurns % 2 == 1;
        int width = ToPixels(sideways ? sizePoints.Height : sizePoints.Width, dpi);
        int height = ToPixels(sideways ? sizePoints.Width : sizePoints.Height, dpi);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            bitmap.SetResolution(dpi, dpi);
            BitmapData pixels = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                lock (PdfiumLibrary.Sync)
                {
                    WithPage(pageIndex, page => RenderInto(page, pixels, pageIndex, quarterTurns));
                }
            }
            finally
            {
                bitmap.UnlockBits(pixels);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Closes the document and frees its native memory. Safe to call more than once.</summary>
    public void Dispose() => _handle.Dispose();

    /// <summary>Converts a length in points to whole pixels at <paramref name="dpi"/>, never less than one.</summary>
    private static int ToPixels(float points, int dpi)
    {
        double pixels = Math.Round(points * dpi / 72.0, MidpointRounding.AwayFromZero);
        if (pixels > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "The page is too large to render at this resolution.");
        }

        return Math.Max(1, (int)pixels);
    }

    /// <summary>
    /// Points PDFium at the locked GDI+ pixels, paints them white and renders the page into them.
    /// Must be called while holding <see cref="PdfiumLibrary.Sync"/>.
    /// </summary>
    private static void RenderInto(IntPtr page, BitmapData pixels, int pageIndex, int quarterTurns)
    {
        // PDFium draws straight into the GDI+ bitmap's memory (same BGRA layout), so no pixel copy
        // is needed. Destroying the PDFium bitmap afterwards leaves that memory untouched.
        IntPtr pdfBitmap = PdfiumNative.FPDFBitmap_CreateEx(pixels.Width, pixels.Height, PdfiumNative.BitmapFormatBgra, pixels.Scan0, pixels.Stride);
        if (pdfBitmap == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium could not create a {pixels.Width} x {pixels.Height} bitmap for page {pageIndex + 1}.");
        }

        try
        {
            // PDF pages have no background of their own: without white paper underneath, every
            // area the page does not paint would stay transparent black.
            PdfiumNative.FPDFBitmap_FillRect(pdfBitmap, 0, 0, pixels.Width, pixels.Height, PdfiumNative.ColorWhite);
            PdfiumNative.FPDF_RenderPageBitmap(
                pdfBitmap,
                page,
                0,
                0,
                pixels.Width,
                pixels.Height,
                quarterTurns,
                PdfiumNative.RenderAnnotations | PdfiumNative.RenderForPrinting);
        }
        finally
        {
            PdfiumNative.FPDFBitmap_Destroy(pdfBitmap);
        }
    }

    /// <summary>
    /// Loads a page, runs <paramref name="action"/> on it and always closes it again.
    /// Must be called while holding <see cref="PdfiumLibrary.Sync"/>.
    /// </summary>
    private void WithPage(int pageIndex, Action<IntPtr> action)
    {
        UseHandle(document =>
        {
            IntPtr page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
            if (page == IntPtr.Zero)
            {
                throw new InvalidOperationException($"PDFium could not load page {pageIndex + 1}; the page may be damaged.");
            }

            try
            {
                action(page);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }

            return 0;
        });
    }

    /// <summary>
    /// Runs <paramref name="use"/> with the raw document pointer while holding a reference on the
    /// safe handle, so a concurrent <see cref="Dispose"/> cannot close the document mid-call
    /// (and a call after disposal fails cleanly with <see cref="ObjectDisposedException"/>).
    /// </summary>
    private T UseHandle<T>(Func<IntPtr, T> use)
    {
        bool added = false;
        try
        {
            _handle.DangerousAddRef(ref added);
            return use(_handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    /// <summary>PDFium only knows the four quarter turns 0 to 3; anything else is a caller bug.</summary>
    private static void ValidateQuarterTurns(int quarterTurns)
    {
        if ((uint)quarterTurns > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(quarterTurns), quarterTurns, "Quarter turns must be 0, 1, 2 or 3.");
        }
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if ((uint)pageIndex >= (uint)PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {PageCount} page(s); page indexes start at 0.");
        }
    }
}
