using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PDFStream.net.Engine;

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
/// <param name="insertAt">Where the copy goes. PDFStream.net passes the current page count, which appends it.</param>
/// <returns><see langword="true"/> when PDFium made the copy.</returns>
internal delegate bool PageCopier(IntPtr document, int pageIndex, int insertAt);

/// <summary>How <see cref="PdfiumDocument"/> prepared a page for printing the first time it was drawn.</summary>
internal enum PagePreparation
{
    /// <summary>The page has no form fields; it is drawn exactly as PDFium draws it.</summary>
    NoFormFields,

    /// <summary>The page's fields were flattened into a copy of the page, which is drawn in its place.</summary>
    Copy,

    /// <summary>
    /// The copy could not be used, so the fields were flattened into the page itself. A page that inherits its
    /// /Resources from the page tree loses what it draws through them (the documented limitation).
    /// </summary>
    InPlace,

    /// <summary>The fields could not be flattened at all; the page is drawn as it is, its fields blank.</summary>
    DrawnAsItIs,
}

/// <summary>
/// A PDF opened by PDFium. Every native call is made while holding
/// <see cref="PdfiumLibrary.Sync"/>, so the document can be used from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Form fields. PDFium's two page-drawing calls never draw form fields (widget annotations), so a filled-in
/// form would print blank. The first time a page is drawn, PDFStream.net therefore checks whether it has form
/// fields and, if it does, prepares it inside PDFStream.net's own in-memory copy of the PDF (the caller's file or
/// bytes are never changed):
/// </para>
/// <list type="number">
/// <item>The document's form-fill environment looks at the page and builds the appearances that are not
/// stored in the file (an annotation without one, or every field when the form says /NeedAppearances), so
/// nothing that PDFium would draw on the fly is lost when the annotations become page content.</item>
/// <item>The page is copied to the end of the in-memory document (<c>FPDF_ImportPagesByIndex</c> with the
/// document as both source and destination). The copy carries, written on itself, what the page inherits
/// from the page tree (its MediaBox, CropBox, Rotate and Resources), and its own copies of the objects it
/// uses. The page itself is not flattened if it can be avoided: flattening adds its result to the page's own
/// /Resources, creating an empty one when the page inherits them, and that empty dictionary hides the
/// inherited fonts, images and XObjects, so the page content that uses them would disappear from the
/// paper.</item>
/// <item>The copy and the page are drawn small and compared (within a fixed pixel budget; a page too odd in size or
/// shape to compare counts as not verified). The copy is used only if it draws exactly the same pixels. It does not when something on the page depends on which object it refers to rather than on
/// what the object contains; optional content (layers) is the known case, because the document's layer
/// settings name the page's layer objects, not the copy's, so a layer the PDF switches off would show on the
/// copy. Such a page has its fields flattened into the page itself instead, as before the copy was introduced,
/// which keeps its layers right. If that page also inherits its /Resources, the inherited ones are hidden as
/// described above: that combination is the one case this does not solve.</item>
/// <item>The visible box of the copy (or the page) is written onto it as its CropBox (flattening rewrites the
/// MediaBox and the CropBox from the page's own entries), and <c>FPDFPage_Flatten</c> with FLAT_PRINT draws
/// every annotation marked for printing into its content (fields not marked for printing are left out, as on
/// paper).</item>
/// </list>
/// <para>
/// From then on the copy (or the flattened page) is drawn for the page, through the vector path as before,
/// fields included. The copy lies after the last page the document reports, so no page index reaches it:
/// <see cref="PageCount"/> and the page sizes stay those of the PDF. Pages without form fields are never
/// touched. A copy that cannot be used for any reason (it could not be made, it is not exactly the size of the
/// page, it lost annotations, it draws differently, or its flatten failed) is treated like a copy that draws
/// differently: the fields are flattened into the page itself. Only when that fails too is the page drawn as it
/// is (its fields blank, as before form fields were handled), so preparing a page never makes a print fail.
/// The cost falls on pages with fields only: the page is copied, drawn twice at low resolution for the
/// comparison and parsed once more, and the copy keeps its own copy of the objects it uses (its fonts and
/// images, for example) in memory until the document is closed. XFA-only forms have no fields of this kind and
/// still print only their page content; the bundled PDFium is built without XFA.
/// </para>
/// </remarks>
internal sealed class PdfiumDocument : IPdfDocument
{
    /// <summary>A page whose form fields have not been looked at yet (see <see cref="_drawnFrom"/>).</summary>
    private const int NotPrepared = -1;

    /// <summary>The page index <see cref="TryPrepareForPrinting"/> returns when the loaded page must be drawn as it is.</summary>
    private const int DrawAsItIs = -1;

    /// <summary>What <see cref="TryFlattenIntoCopy"/> returns when the page's copy cannot be used.</summary>
    private const int NoCopy = -1;

    /// <summary>The resolution at which a page and its copy are drawn to check that they look the same.</summary>
    private const int ComparisonDpi = 72;

    /// <summary>The most pixels either comparison drawing may have; larger pages are compared at a lower resolution.</summary>
    internal const int ComparisonMaxPixels = 1_000_000;

    /// <summary>The most pixels either side of a comparison drawing may have, whatever the shape of the page.</summary>
    internal const int ComparisonMaxSide = 4096;

    /// <summary>The most bytes one comparison drawing may take (four per pixel, within the pixel budget).</summary>
    private const long ComparisonMaxBytes = 4L * ComparisonMaxPixels;

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

    /// <summary>
    /// Test hook: called with the pixel width and height of every picture drawn to compare a form page with its
    /// copy, just before it is drawn, while the PDFium lock is held. Lets a test check that the comparison never
    /// draws more than its budget, whatever the page size. <see langword="null"/> (the default) in production.
    /// </summary>
    internal Action<int, int>? ComparisonRendered { get; set; }

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
    /// The page is rendered with the printing flag. In PDFStream.net a page bitmap is only ever the
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
                    (int drawFrom, _) = TryPrepareForPrinting(document, pageIndex, page);
                    IntPtr prepared = drawFrom == DrawAsItIs ? IntPtr.Zero : PdfiumNative.FPDF_LoadPage(document, drawFrom);
                    if (prepared != IntPtr.Zero)
                    {
                        _drawnFrom[pageIndex] = drawFrom;
                        PdfiumNative.FPDF_ClosePage(page);
                        page = prepared;
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
    /// If the page has form fields, makes every annotation marked for printing part of the content that is drawn
    /// for the page: in a copy of the page at the end of the document when the copy can be used, otherwise in the
    /// page itself (see the class remarks for the steps and why each is needed). Must be called while holding
    /// <see cref="PdfiumLibrary.Sync"/> and a reference on the handle. Never throws.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="pageIndex">The page to prepare.</param>
    /// <param name="page">The page, loaded. It is not closed here, and is drawn as it is when this fails.</param>
    /// <returns>
    /// How the page was prepared, and the page of PDFium's document to draw for it from now on: the printable
    /// copy, or the page itself when its fields were flattened into it (load it again: the loaded page still
    /// holds the old content). For <see cref="PagePreparation.NoFormFields"/> and
    /// <see cref="PagePreparation.DrawnAsItIs"/> the index is <see cref="DrawAsItIs"/>: the loaded page is drawn
    /// as it is.
    /// </returns>
    private (int DrawFrom, PagePreparation How) TryPrepareForPrinting(IntPtr document, int pageIndex, IntPtr page)
    {
        try
        {
            // Only pages that really carry form fields are prepared: every other page is drawn exactly as
            // PDFium draws it. Checked per page, not from the catalog's /AcroForm, because pages copied out of
            // a form by another tool often keep their fields but lose /AcroForm.
            if (!HasFormFields(page))
            {
                return (DrawAsItIs, PagePreparation.NoFormFields);
            }

            IntPtr environment = _handle.GetOrCreateFormFillEnvironment();
            if (environment == IntPtr.Zero)
            {
                // Without the form-fill module, annotations whose appearance PDFium builds only while drawing
                // would be dropped by flattening. Drawing the page as it is loses only the fields.
                return (DrawAsItIs, PagePreparation.DrawnAsItIs);
            }

            // Showing the page to the form-fill module and taking it away again builds the missing appearances
            // and stores them in the annotations, before the copy below copies the annotations; nothing else of
            // the module is used.
            PdfiumNative.FORM_OnAfterLoadPage(page, environment);
            PdfiumNative.FORM_OnBeforeClosePage(page, environment);
        }
        catch (Exception)
        {
            // Preparing a page is an improvement, never a requirement: if even the check for fields fails (for
            // example a pdfium.dll without one of these functions), the page is drawn as it is.
            return (DrawAsItIs, PagePreparation.DrawnAsItIs);
        }

        int copyIndex = TryFlattenIntoCopy(document, pageIndex, page);
        if (copyIndex != NoCopy)
        {
            return (copyIndex, PagePreparation.Copy);
        }

        // The copy could not be used, for whatever reason (the known one is optional content, see the class
        // remarks; others are a copy that lost its annotations, a copy of another size, a failed flatten, or a
        // pdfium.dll without the copy function). Flatten the fields into the page itself instead, as before the
        // copy was introduced: the fields print, the layers stay right, and only a page that also inherits its
        // /Resources loses what it draws through them. Drawing the page as it is, with its fields blank, is
        // left for a page whose fields cannot be flattened at all.
        return TryFlattenInPlace(page) ? (pageIndex, PagePreparation.InPlace) : (DrawAsItIs, PagePreparation.DrawnAsItIs);
    }

    /// <summary>
    /// Copies the page to the end of the document, checks that the copy can stand in for it, and flattens the
    /// copy's annotations for printing. Must be called while holding <see cref="PdfiumLibrary.Sync"/> and a
    /// reference on the handle, after the appearance pass. Never throws: every failure means "no usable copy".
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="pageIndex">The page to copy.</param>
    /// <param name="page">The page, loaded (it serves the comparison and the annotation count).</param>
    /// <returns>The index of the flattened copy, or <see cref="NoCopy"/> when the copy cannot be used.</returns>
    private int TryFlattenIntoCopy(IntPtr document, int pageIndex, IntPtr page)
    {
        try
        {
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
                // PDFium's page copy drops a whole /Annots array when one of its entries cannot be resolved (a
                // damaged reference), leaving a copy without any fields. The pixel comparison below cannot see
                // that, because it draws without the fields, so the annotations are counted first: a copy that
                // does not carry every annotation of the page is not used.
                if (PdfiumNative.FPDFPage_GetAnnotCount(copy) != PdfiumNative.FPDFPage_GetAnnotCount(page)
                    || !CopyDrawsLikeThePage(document, page, pageIndex, copy))
                {
                    return NoCopy;
                }

                flattened = PinVisibleBoxAndFlatten(copy);
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
            // Any failure on the copy path (for example a pdfium.dll without FPDF_ImportPagesByIndex, which is
            // an experimental PDFium function) only means the copy is not used.
            return NoCopy;
        }
    }

    /// <summary>
    /// Flattens the page's annotations for printing into the page itself (the fallback when its copy cannot be
    /// used). Must be called while holding <see cref="PdfiumLibrary.Sync"/>. Never throws.
    /// </summary>
    /// <param name="page">The loaded page. Load it again afterwards to draw the flattened content.</param>
    /// <returns><see langword="true"/> when PDFium flattened the page.</returns>
    private bool TryFlattenInPlace(IntPtr page)
    {
        try
        {
            return PinVisibleBoxAndFlatten(page) == PdfiumNative.FlattenSuccess;
        }
        catch (Exception)
        {
            // For example a pdfium.dll without FPDFPage_Flatten: the page is then drawn as it is.
            return false;
        }
    }

    /// <summary>
    /// Writes the loaded page's visible box onto it and flattens its annotations for printing. Flattening rewrites
    /// the MediaBox and CropBox from the page's own entries, so an inherited box would otherwise become US Letter
    /// (a copy already carries its boxes; writing the visible box as well keeps flattening from changing what
    /// part of the page is shown).
    /// </summary>
    /// <param name="page">The loaded page. Load it again afterwards to draw the flattened content.</param>
    /// <returns>What the flattener returned, or FLATTEN_FAIL (0) when the box could not be read.</returns>
    private int PinVisibleBoxAndFlatten(IntPtr page)
    {
        if (PdfiumNative.FPDF_GetPageBoundingBox(page, out PdfiumNative.FsRectF box) == 0)
        {
            return 0;
        }

        PdfiumNative.FPDFPage_SetCropBox(page, box.Left, box.Bottom, box.Right, box.Top);
        if (PdfiumNative.FPDFPage_GetMediaBox(page, out _, out _, out _, out _) == 0)
        {
            PdfiumNative.FPDFPage_SetMediaBox(page, box.Left, box.Bottom, box.Right, box.Top);
        }

        return _flattenPage(page, PdfiumNative.FlattenForPrint);
    }

    /// <summary>
    /// Whether the (not yet flattened) copy of a page draws exactly the same pixels as the page itself: both are
    /// rendered for printing, annotations included, at up to <see cref="ComparisonDpi"/> dpi and at most
    /// <see cref="ComparisonMaxPixels"/> pixels, and compared byte for byte.
    /// </summary>
    /// <remarks>
    /// The copy holds its own copies of the objects the page uses. That is what keeps inherited resources, but
    /// anything whose meaning depends on which object it is, rather than on what it contains, changes: optional
    /// content is the known case. Comparing the two drawings catches every such difference that shows, without
    /// having to know its cause, and costs two small renders, once, for pages with form fields only.
    /// </remarks>
    /// <param name="document">The document.</param>
    /// <param name="page">The page, loaded.</param>
    /// <param name="pageIndex">The page's index, for its size.</param>
    /// <param name="copy">The page's copy, loaded and not yet flattened.</param>
    /// <returns>
    /// <see langword="true"/> only when the comparison was made and the pictures are identical. Anything else (the
    /// pictures differ, the page is too odd in size or shape to compare, or anything in the comparison fails)
    /// is <see langword="false"/>, "could not verify", and never an exception: the caller then takes its safe
    /// fallback.
    /// </returns>
    private bool CopyDrawsLikeThePage(IntPtr document, IntPtr page, int pageIndex, IntPtr copy)
    {
        try
        {
            if (PdfiumNative.FPDF_GetPageSizeByIndexF(document, pageIndex, out PdfiumNative.FsSizeF size) == 0
                || !TryGetComparisonSize(size.Width, size.Height, out int width, out int height))
            {
                return false;
            }

            ComparisonRendered?.Invoke(width, height);
            byte[] pagePixels = RenderForComparison(page, width, height);
            ComparisonRendered?.Invoke(width, height);
            return RenderForComparison(copy, width, height).AsSpan().SequenceEqual(pagePixels);
        }
        catch (Exception)
        {
            // The comparison is a safety check, not part of printing: if it cannot be made (no memory for the
            // pictures, a page PDFium cannot draw, a size refused below), the copy is simply not verified.
            return false;
        }
    }

    /// <summary>
    /// The pixel size at which a page of the given size is drawn for the comparison: one pixel per point
    /// (<see cref="ComparisonDpi"/>), scaled down so that neither side exceeds <see cref="ComparisonMaxSide"/>
    /// and the area stays within <see cref="ComparisonMaxPixels"/>.
    /// </summary>
    /// <remarks>
    /// The page size comes from the PDF, which may be damaged or hostile: a MediaBox can be enormous in one
    /// direction and tiny in the other, or not a usable number at all. So the size is checked first, all the
    /// arithmetic is done in <see langword="double"/>, each side is clamped before the area, and the result is
    /// rounded down so it can never round up over a limit. A page whose size is not finite and positive, or that
    /// is so small or so long and thin that a side would be under one pixel, is not compared: its content would
    /// collapse into a single row or column, which proves nothing.
    /// </remarks>
    /// <param name="widthPoints">The page width in points, as PDFium reports it.</param>
    /// <param name="heightPoints">The page height in points, as PDFium reports it.</param>
    /// <param name="width">The comparison picture's width in pixels, 1 to <see cref="ComparisonMaxSide"/>.</param>
    /// <param name="height">The comparison picture's height in pixels, 1 to <see cref="ComparisonMaxSide"/>.</param>
    /// <returns><see langword="false"/> when the page must not be compared.</returns>
    internal static bool TryGetComparisonSize(float widthPoints, float heightPoints, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!float.IsFinite(widthPoints) || !float.IsFinite(heightPoints) || widthPoints <= 0 || heightPoints <= 0)
        {
            return false;
        }

        double scale = ComparisonDpi / 72.0;
        double targetWidth = widthPoints * scale;
        double targetHeight = heightPoints * scale;

        // First neither side over the cap (this alone tames a very long, thin page), then the area.
        double fitSides = Math.Min(1.0, Math.Min(ComparisonMaxSide / targetWidth, ComparisonMaxSide / targetHeight));
        targetWidth *= fitSides;
        targetHeight *= fitSides;
        double area = targetWidth * targetHeight;
        if (area > ComparisonMaxPixels)
        {
            double fitArea = Math.Sqrt(ComparisonMaxPixels / area);
            targetWidth *= fitArea;
            targetHeight *= fitArea;
        }

        if (targetWidth < 1 || targetHeight < 1)
        {
            return false;
        }

        // Both values are now between 1 and ComparisonMaxSide, so the conversions cannot overflow, and rounding
        // down keeps the area within the budget.
        width = Math.Min(ComparisonMaxSide, (int)Math.Floor(targetWidth));
        height = Math.Min(ComparisonMaxSide, (int)Math.Floor(targetHeight));
        return true;
    }

    /// <summary>Renders a loaded page for printing onto white, into a new array of BGRA pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size is outside the comparison budget (a second line of
    /// defence behind <see cref="TryGetComparisonSize"/>).</exception>
    private static byte[] RenderForComparison(IntPtr page, int width, int height)
    {
        // Computed in long and checked against the hard limit before anything is allocated.
        long stride = (long)width * 4;
        long length = stride * height;
        if (width < 1 || height < 1 || width > ComparisonMaxSide || height > ComparisonMaxSide || length > ComparisonMaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A {width} x {height} comparison picture is outside the budget.");
        }

        var pixels = new byte[length];

        // Pinned only for the duration of this one render, so PDFium can draw straight into the array.
        GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            IntPtr bitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.BitmapFormatBgra, pin.AddrOfPinnedObject(), (int)stride);
            if (bitmap == IntPtr.Zero)
            {
                throw new InvalidOperationException($"PDFium could not create a {width} x {height} bitmap to compare a page with its copy.");
            }

            try
            {
                PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, PdfiumNative.ColorWhite);
                PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, PdfiumNative.RenderAnnotations | PdfiumNative.RenderForPrinting);
            }
            finally
            {
                PdfiumNative.FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            pin.Free();
        }

        return pixels;
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
