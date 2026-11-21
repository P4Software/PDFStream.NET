using System.Drawing;
using System.Drawing.Imaging;

namespace PDFStream.Engine;

/// <summary>
/// Flattens one loaded page's annotations into its content (PDFium's <c>FPDFPage_Flatten</c> in
/// production). A seam so tests can make flattening fail.
/// </summary>
/// <param name="page">The loaded page (an FPDF_PAGE).</param>
/// <param name="usage">FLAT_NORMALDISPLAY (0) or FLAT_PRINT (1).</param>
/// <returns>FLATTEN_FAIL (0), FLATTEN_SUCCESS (1) or FLATTEN_NOTHINGTODO (2).</returns>
internal delegate int PageFlattener(IntPtr page, int usage);

/// <summary>
/// A PDF opened by PDFium. Every native call is made while holding
/// <see cref="PdfiumLibrary.Sync"/>, so the document can be used from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Form fields. PDFium's two page-drawing calls never draw form fields (widget annotations), so a filled-in
/// form would print blank. The first time a page is drawn, PDFStream therefore checks whether it has form
/// fields and, if it does, makes them part of the page, as they print, in PDFStream's own in-memory copy of
/// the PDF (the caller's file or bytes are never changed):
/// </para>
/// <list type="number">
/// <item>The document's form-fill environment looks at the page and builds the appearances that are not
/// stored in the file (an annotation without one, or every field when the form says /NeedAppearances), so
/// nothing that PDFium would draw on the fly is lost by the next step.</item>
/// <item>The page's visible box is written onto the page itself, because flattening rewrites the MediaBox and
/// CropBox from the page's own entries and would otherwise turn an inherited box into US Letter.</item>
/// <item><c>FPDFPage_Flatten</c> with FLAT_PRINT draws every annotation marked for printing into the page
/// content (fields not marked for printing are left out, as on paper), and the page is loaded again.</item>
/// </list>
/// <para>
/// The page then draws through the vector path as before, fields included. Pages without form fields are
/// never touched. If any step fails, the page is drawn as it is (its fields blank, as before this was
/// added), so preparing a page never makes a print fail. XFA-only forms have no fields of this kind and
/// still print only their page content; the bundled PDFium is built without XFA.
/// </para>
/// </remarks>
internal sealed class PdfiumDocument : IPdfDocument
{
    private readonly PdfiumDocumentHandle _handle;
    private readonly PageFlattener _flattenPage;

    // One flag per page, read and written only while holding PdfiumLibrary.Sync. Set the first time a page is
    // drawn, once it has been checked for form fields (and flattened if it had any), so the work is done once
    // per page however often the page is drawn (copies, the bitmap fallback).
    private readonly bool[] _pagePrepared;

    /// <summary>
    /// Wraps an open handle. The caller must hold <see cref="PdfiumLibrary.Sync"/>, because the page
    /// count is read from PDFium here, once, instead of on every access.
    /// </summary>
    /// <param name="handle">The open document; this object takes ownership of it.</param>
    /// <param name="flattenPage">How a page's annotations are flattened; <see langword="null"/> uses
    /// PDFium's <c>FPDFPage_Flatten</c>. Tests pass one that fails.</param>
    internal PdfiumDocument(PdfiumDocumentHandle handle, PageFlattener? flattenPage = null)
    {
        _handle = handle;
        _flattenPage = flattenPage ?? PdfiumNative.FPDFPage_Flatten;
        PageCount = PdfiumNative.FPDF_GetPageCount(handle.DangerousGetHandle());
        _pagePrepared = new bool[Math.Max(0, PageCount)];
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
    /// Loads a page for drawing, prepares its form fields the first time (see the class remarks), runs
    /// <paramref name="action"/> on it and always closes it again.
    /// Must be called while holding <see cref="PdfiumLibrary.Sync"/>.
    /// </summary>
    private void WithPage(int pageIndex, Action<IntPtr> action)
    {
        UseHandle(document =>
        {
            IntPtr page = LoadPage(document, pageIndex);
            try
            {
                if (!_pagePrepared[pageIndex])
                {
                    // Marked before the work, so a page whose preparation fails is drawn as it is and not
                    // tried again on every copy.
                    _pagePrepared[pageIndex] = true;
                    if (TryFlattenFormFieldsForPrinting(page))
                    {
                        // Flattening rewrote the page in the document, but the page object loaded above still
                        // holds the content it parsed before. Load the page again to draw the new content.
                        PdfiumNative.FPDF_ClosePage(page);
                        page = IntPtr.Zero;
                        page = LoadPage(document, pageIndex);
                    }
                }

                action(page);
            }
            finally
            {
                if (page != IntPtr.Zero)
                {
                    PdfiumNative.FPDF_ClosePage(page);
                }
            }

            return 0;
        });
    }

    /// <summary>Loads a page or throws the engine's documented "could not draw" exception.</summary>
    private static IntPtr LoadPage(IntPtr document, int pageIndex)
    {
        IntPtr page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
        if (page == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium could not load page {pageIndex + 1}; the page may be damaged.");
        }

        return page;
    }

    /// <summary>
    /// If the page has form fields, makes every annotation marked for printing part of the page content (see
    /// the class remarks for the three steps and why each is needed). Must be called while holding
    /// <see cref="PdfiumLibrary.Sync"/> and a reference on the handle.
    /// </summary>
    /// <param name="page">The loaded page. It is not closed here.</param>
    /// <returns>
    /// <see langword="true"/> when the page content was changed, so the page must be loaded again before it is
    /// drawn; <see langword="false"/> when the page had no form fields or could not be prepared, in which case
    /// it is drawn as it is.
    /// </returns>
    private bool TryFlattenFormFieldsForPrinting(IntPtr page)
    {
        try
        {
            // Only pages that really carry form fields are changed: every other page is drawn exactly as
            // PDFium draws it. Checked per page, not from the catalog's /AcroForm, because pages copied out of
            // a form by another tool often keep their fields but lose /AcroForm.
            if (!HasFormFields(page))
            {
                return false;
            }

            IntPtr environment = _handle.GetOrCreateFormFillEnvironment();
            if (environment == IntPtr.Zero)
            {
                // Without the form-fill module, annotations whose appearance PDFium builds only while drawing
                // would be dropped by flattening. Drawing the page as it is loses only the fields.
                return false;
            }

            // Showing the page to the form-fill module and taking it away again builds and stores the missing
            // appearances; nothing else of the module is used.
            PdfiumNative.FORM_OnAfterLoadPage(page, environment);
            PdfiumNative.FORM_OnBeforeClosePage(page, environment);

            // Flattening rewrites the MediaBox and CropBox from the page's own entries (a missing MediaBox
            // becomes US Letter), so write the page's visible box, inherited or not, onto the page first. The
            // page then keeps exactly its size and position.
            if (PdfiumNative.FPDF_GetPageBoundingBox(page, out PdfiumNative.FsRectF box) == 0)
            {
                return false;
            }

            PdfiumNative.FPDFPage_SetCropBox(page, box.Left, box.Bottom, box.Right, box.Top);
            if (PdfiumNative.FPDFPage_GetMediaBox(page, out _, out _, out _, out _) == 0)
            {
                PdfiumNative.FPDFPage_SetMediaBox(page, box.Left, box.Bottom, box.Right, box.Top);
            }

            return _flattenPage(page, PdfiumNative.FlattenForPrint) == PdfiumNative.FlattenSuccess;
        }
        catch (Exception)
        {
            // Preparing a page is an improvement, never a requirement: whatever goes wrong here (for example
            // a pdfium.dll without one of these functions), the page is drawn as it is and the print goes on.
            return false;
        }
    }

    /// <summary>Whether the loaded page has at least one widget (form field) annotation.</summary>
    private static bool HasFormFields(IntPtr page)
    {
        int count = PdfiumNative.FPDFPage_GetAnnotCount(page);
        for (int index = 0; index < count; index++)
        {
            IntPtr annotation = PdfiumNative.FPDFPage_GetAnnot(page, index);
            if (annotation == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                if (PdfiumNative.FPDFAnnot_GetSubtype(annotation) == PdfiumNative.AnnotationWidget)
                {
                    return true;
                }
            }
            finally
            {
                PdfiumNative.FPDFPage_CloseAnnot(annotation);
            }
        }

        return false;
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
