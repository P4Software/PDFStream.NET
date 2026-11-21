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
/// Copies one page of a document to a new position in the same document (PDFium's
/// <c>FPDF_ImportPagesByIndex</c>, with the document as both source and destination, in production). A seam so
/// tests can make copying fail or change the copy.
/// </summary>
/// <param name="document">The document (an FPDF_DOCUMENT); the page is copied within it.</param>
/// <param name="pageIndex">The page to copy.</param>
/// <param name="insertAt">Where the copy goes. PDFStream passes the current page count, which appends it.</param>
/// <returns><see langword="true"/> when PDFium made the copy.</returns>
internal delegate bool PageCopier(IntPtr document, int pageIndex, int insertAt);

/// <summary>
/// A PDF opened by PDFium. Every native call is made while holding
/// <see cref="PdfiumLibrary.Sync"/>, so the document can be used from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Form fields. PDFium's two page-drawing calls never draw form fields (widget annotations), so a filled-in
/// form would print blank. The first time a page is drawn, PDFStream therefore checks whether it has form
/// fields and, if it does, prepares a printable copy of the page inside PDFStream's own in-memory copy of the
/// PDF (the caller's file or bytes are never changed):
/// </para>
/// <list type="number">
/// <item>The document's form-fill environment looks at the page and builds the appearances that are not
/// stored in the file (an annotation without one, or every field when the form says /NeedAppearances), so
/// nothing that PDFium would draw on the fly is lost when the annotations become page content.</item>
/// <item>The page is copied to the end of the in-memory document (<c>FPDF_ImportPagesByIndex</c> with the
/// document as both source and destination). The copy carries, written on itself, what the page inherits
/// from the page tree (its MediaBox, CropBox, Rotate and Resources), and its own copies of the objects it
/// uses. The page itself is not flattened: flattening adds its result to the page's own /Resources, creating an
/// empty one when the page inherits them, and that empty dictionary hides the inherited fonts, images and
/// XObjects, so the page content that uses them would disappear from the paper.</item>
/// <item>The copy's visible box is written onto it as its CropBox (flattening rewrites the MediaBox and the
/// CropBox from the page's own entries), and <c>FPDFPage_Flatten</c> with FLAT_PRINT draws every annotation
/// marked for printing into the copy's content (fields not marked for printing are left out, as on
/// paper).</item>
/// </list>
/// <para>
/// From then on the copy is drawn in place of the page, through the vector path as before, fields included.
/// The copy lies after the last page the document reports, so no page index reaches it: <see cref="PageCount"/>
/// and the page sizes stay those of the PDF. Pages without form fields are never touched. If any step fails,
/// or the copy is not exactly the size of the page, the page itself is drawn as it is (its fields blank, as
/// before this was added), so preparing a page never makes a print fail. The cost falls on pages with fields
/// only: the page is copied and parsed once more, and the copy keeps its own copy of the objects it uses (its
/// fonts and images, for example) in memory until the document is closed. XFA-only forms have no fields of
/// this kind and still print only their page content; the bundled PDFium is built without XFA.
/// </para>
/// </remarks>
internal sealed class PdfiumDocument : IPdfDocument
{
    /// <summary>A page whose form fields have not been looked at yet (see <see cref="_drawnFrom"/>).</summary>
    private const int NotPrepared = -1;

    /// <summary>What <see cref="TryCreatePrintableCopy"/> returns when the page itself must be drawn.</summary>
    private const int NoCopy = -1;

    private readonly PdfiumDocumentHandle _handle;
    private readonly PageFlattener _flattenPage;
    private readonly PageCopier _copyPage;

    // For each page the caller can see, which page of PDFium's document is drawn for it: NotPrepared until the
    // page is first drawn, then the page itself, or the index of its printable copy when it has form fields.
    // Read and written only while holding PdfiumLibrary.Sync, so the work is done once per page however often
    // and from however many threads the page is drawn (copies, the bitmap fallback).
    private readonly int[] _drawnFrom;

    /// <summary>
    /// Wraps an open handle. The caller must hold <see cref="PdfiumLibrary.Sync"/>, because the page
    /// count is read from PDFium here, once, instead of on every access.
    /// </summary>
    /// <param name="handle">The open document; this object takes ownership of it.</param>
    /// <param name="flattenPage">How a page's annotations are flattened; <see langword="null"/> uses
    /// PDFium's <c>FPDFPage_Flatten</c>. Tests pass one that fails.</param>
    /// <param name="copyPage">How a page with form fields is copied within the document before it is flattened;
    /// <see langword="null"/> uses PDFium's <c>FPDF_ImportPagesByIndex</c>. Tests pass one that fails.</param>
    internal PdfiumDocument(PdfiumDocumentHandle handle, PageFlattener? flattenPage = null, PageCopier? copyPage = null)
    {
        _handle = handle;
        _flattenPage = flattenPage ?? PdfiumNative.FPDFPage_Flatten;
        _copyPage = copyPage ?? CopyWithinDocument;

        // Read once: the printable copies of form pages are added after the last page, and must never become
        // visible as extra pages.
        PageCount = PdfiumNative.FPDF_GetPageCount(handle.DangerousGetHandle());
        _drawnFrom = new int[Math.Max(0, PageCount)];
        Array.Fill(_drawnFrom, NotPrepared);
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
    /// Loads what is drawn for a page (the page itself, or its printable copy once it has one), prepares the
    /// page the first time it is drawn (see the class remarks), runs <paramref name="action"/> on it and always
    /// closes it again. Must be called while holding <see cref="PdfiumLibrary.Sync"/>.
    /// </summary>
    private void WithPage(int pageIndex, Action<IntPtr> action)
    {
        UseHandle(document =>
        {
            bool firstDraw = _drawnFrom[pageIndex] == NotPrepared;
            IntPtr page = LoadPage(document, firstDraw ? pageIndex : _drawnFrom[pageIndex], pageIndex);
            try
            {
                if (firstDraw)
                {
                    // Marked before the work, so a page whose preparation fails is drawn as it is and not
                    // tried again on every copy.
                    _drawnFrom[pageIndex] = pageIndex;

                    // The page loaded above serves the form-field check, so a page without fields is loaded
                    // and parsed only once, as before form fields were handled.
                    int copyIndex = TryCreatePrintableCopy(document, pageIndex, page);
                    IntPtr copy = copyIndex == NoCopy ? IntPtr.Zero : PdfiumNative.FPDF_LoadPage(document, copyIndex);
                    if (copy != IntPtr.Zero)
                    {
                        _drawnFrom[pageIndex] = copyIndex;
                        PdfiumNative.FPDF_ClosePage(page);
                        page = copy;
                    }
                }

                action(page);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }

            return 0;
        });
    }

    /// <summary>Loads a page or throws the engine's documented "could not draw" exception.</summary>
    /// <param name="document">The document.</param>
    /// <param name="index">The page of PDFium's document to load: the page itself or its printable copy.</param>
    /// <param name="pageIndex">The page the caller asked for, which is the one the message names.</param>
    private static IntPtr LoadPage(IntPtr document, int index, int pageIndex)
    {
        IntPtr page = PdfiumNative.FPDF_LoadPage(document, index);
        if (page == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium could not load page {pageIndex + 1}; the page may be damaged.");
        }

        return page;
    }

    /// <summary>
    /// If the page has form fields, makes a copy of it at the end of the document in which every annotation
    /// marked for printing is part of the page content (see the class remarks for the three steps and why each
    /// is needed). Must be called while holding <see cref="PdfiumLibrary.Sync"/> and a reference on the handle.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="pageIndex">The page to prepare.</param>
    /// <param name="page">The page, loaded. It is not closed here, and is drawn as it is when this fails.</param>
    /// <returns>
    /// The index of the printable copy, to be drawn in place of the page; or <see cref="NoCopy"/> when the page
    /// has no form fields or could not be prepared, in which case the page itself is drawn as it is.
    /// </returns>
    private int TryCreatePrintableCopy(IntPtr document, int pageIndex, IntPtr page)
    {
        try
        {
            // Only pages that really carry form fields are prepared: every other page is drawn exactly as
            // PDFium draws it. Checked per page, not from the catalog's /AcroForm, because pages copied out of
            // a form by another tool often keep their fields but lose /AcroForm.
            if (!HasFormFields(page))
            {
                return NoCopy;
            }

            IntPtr environment = _handle.GetOrCreateFormFillEnvironment();
            if (environment == IntPtr.Zero)
            {
                // Without the form-fill module, annotations whose appearance PDFium builds only while drawing
                // would be dropped by flattening. Drawing the page as it is loses only the fields.
                return NoCopy;
            }

            // Showing the page to the form-fill module and taking it away again builds the missing appearances
            // and stores them in the annotations, before the copy below copies the annotations; nothing else of
            // the module is used.
            PdfiumNative.FORM_OnAfterLoadPage(page, environment);
            PdfiumNative.FORM_OnBeforeClosePage(page, environment);

            // The copy goes after every page there is, so it can never take the place of a page the caller sees
            // (PageCount was read when the document was opened and does not change).
            int copyIndex = PdfiumNative.FPDF_GetPageCount(document);
            if (!_copyPage(document, pageIndex, copyIndex)
                || PdfiumNative.FPDF_GetPageCount(document) != copyIndex + 1
                || !HaveSameSize(document, pageIndex, copyIndex))
            {
                return NoCopy;
            }

            IntPtr copy = PdfiumNative.FPDF_LoadPage(document, copyIndex);
            if (copy == IntPtr.Zero)
            {
                return NoCopy;
            }

            int flattened;
            try
            {
                // Flattening rewrites the MediaBox and CropBox from the page's own entries (a missing MediaBox
                // becomes US Letter). The copy already carries its boxes, inherited ones included; writing its
                // visible box onto it as well keeps flattening from changing what part of the page is shown.
                if (PdfiumNative.FPDF_GetPageBoundingBox(copy, out PdfiumNative.FsRectF box) == 0)
                {
                    return NoCopy;
                }

                PdfiumNative.FPDFPage_SetCropBox(copy, box.Left, box.Bottom, box.Right, box.Top);
                if (PdfiumNative.FPDFPage_GetMediaBox(copy, out _, out _, out _, out _) == 0)
                {
                    PdfiumNative.FPDFPage_SetMediaBox(copy, box.Left, box.Bottom, box.Right, box.Top);
                }

                flattened = _flattenPage(copy, PdfiumNative.FlattenForPrint);
            }
            finally
            {
                // The copy loaded here still holds the content it parsed before flattening; the caller loads
                // the copy again to draw the flattened content.
                PdfiumNative.FPDF_ClosePage(copy);
            }

            // The copy replaces the page only if it is still exactly the page's size: whatever is printed must
            // land where the page would have.
            return flattened == PdfiumNative.FlattenSuccess && HaveSameSize(document, pageIndex, copyIndex) ? copyIndex : NoCopy;
        }
        catch (Exception)
        {
            // Preparing a page is an improvement, never a requirement: whatever goes wrong here (for example
            // a pdfium.dll without one of these functions), the page is drawn as it is and the print goes on.
            return NoCopy;
        }
    }

    /// <summary>
    /// Copies a page within its own document with PDFium's page import, the production <see cref="PageCopier"/>.
    /// The import writes the page's inherited attributes onto the copy and gives the copy its own copies of the
    /// objects the page uses, so what flattening changes in the copy (its resources, the appearance streams of
    /// its annotations) is not shared with the page itself.
    /// </summary>
    private static bool CopyWithinDocument(IntPtr document, int pageIndex, int insertAt) =>
        PdfiumNative.FPDF_ImportPagesByIndex(document, document, [pageIndex], 1, insertAt) != 0;

    /// <summary>Whether two pages of the document have exactly the same size as displayed (rotation included).</summary>
    private static bool HaveSameSize(IntPtr document, int first, int second) =>
        PdfiumNative.FPDF_GetPageSizeByIndexF(document, first, out PdfiumNative.FsSizeF a) != 0
        && PdfiumNative.FPDF_GetPageSizeByIndexF(document, second, out PdfiumNative.FsSizeF b) != 0
        && a.Width == b.Width
        && a.Height == b.Height;

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
