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
/// Copies pages of a document, in one call, to a new position in the same document (PDFium's
/// <c>FPDF_ImportPagesByIndex</c>, with the document as both source and destination, in production). A seam so
/// tests can make copying fail, change the copies or count the calls.
/// </summary>
/// <param name="document">The document (an FPDF_DOCUMENT); the pages are copied within it.</param>
/// <param name="pageIndices">The pages to copy, in the order their copies are wanted.</param>
/// <param name="insertAt">Where the first copy goes, the others following it. PDFStream.net passes the current
/// page count, which appends them.</param>
/// <returns><see langword="true"/> when PDFium made the copies.</returns>
internal delegate bool PageCopier(IntPtr document, int[] pageIndices, int insertAt);

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
/// Why a page with form fields was not printed from its copy (<see cref="PagePreparation.InPlace"/> or
/// <see cref="PagePreparation.DrawnAsItIs"/>); <see cref="None"/> when it was, or when it has no fields.
/// </summary>
internal enum CopyRejection
{
    /// <summary>Nothing was rejected: the copy was used, or the page has no form fields.</summary>
    None,

    /// <summary>No copy was made: the PDF is larger than the copy budget.</summary>
    OverBudget,

    /// <summary>No copy of this page was made: it was not among the pages planned when the one copy was made.</summary>
    NotPlanned,

    /// <summary>No copy was made: the copy function failed or threw (for example, missing from pdfium.dll).</summary>
    CopyFailed,

    /// <summary>The copy is not exactly the size of the page.</summary>
    SizeDiffers,

    /// <summary>The copy could not be loaded.</summary>
    CopyNotLoaded,

    /// <summary>The copy carries fewer or more annotations than the page (a damaged reference drops them all).</summary>
    AnnotationsLost,

    /// <summary>The copy and the page draw differently (layers are the known cause), or they could not be compared.</summary>
    LooksDifferent,

    /// <summary>Flattening the copy failed or changed its size.</summary>
    FlattenFailed,

    /// <summary>Form handling itself failed before any copy was looked at (no form-fill environment, or an error).</summary>
    FormHandlingFailed,
}

/// <summary>
/// A PDF opened by PDFium. Every native call is made while holding
/// <see cref="PdfiumLibrary.Sync"/>, so the document can be used from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Form fields. PDFium's two page-drawing calls never draw form fields (widget annotations), so a filled-in
/// form would print blank. PDFStream.net therefore prepares the pages with form fields inside its own in-memory
/// copy of the PDF (the caller's file or bytes are never changed):
/// </para>
/// <list type="number">
/// <item>The first time a page with fields is drawn, the pages about to be drawn (those named by
/// <see cref="PlanToDraw"/>, or every page) are looked at once to find the ones with fields. For each of them the
/// document's form-fill environment builds the appearances that are not stored in the file (an annotation without
/// one, or every field when the form says /NeedAppearances), so nothing that PDFium would draw on the fly is lost
/// when the annotations become page content.</item>
/// <item>All those pages are copied to the end of the in-memory document in one call
/// (<c>FPDF_ImportPagesByIndex</c> with the document as both source and destination). Each copy carries, written
/// on itself, what its page inherits from the page tree (MediaBox, CropBox, Rotate and Resources), and the copies
/// get their own copies of the objects the pages use. Because it is one call, an object that several pages share
/// (one /Resources dictionary for the whole document and its images, for example) is copied once, not once per
/// page. The copy is made at most once per document, and only when the PDF is no larger than the copy budget
/// (<see cref="DefaultCopyBudgetBytes"/>). The pages themselves are not flattened if it can be avoided: flattening
/// adds its result to the page's own /Resources, creating an empty one when the page inherits them, and that empty
/// dictionary hides the inherited fonts, images and XObjects, so the page content that uses them would disappear
/// from the paper.</item>
/// <item>When a page with fields is drawn for the first time, its copy is checked. It must carry as many
/// annotations as the page (PDFium's copy drops a whole annotation list when one entry cannot be resolved), and
/// the copy and the page are drawn small and compared (within a fixed pixel budget; a page too odd in size or
/// shape to compare counts as not verified). The copy is used only if it draws exactly the same pixels. It does
/// not when something on the page depends on which object it refers to rather than on what the object contains;
/// optional content (layers) is the known case, because the document's layer settings name the page's layer
/// objects, not the copy's, so a layer the PDF switches off would show on the copy.</item>
/// <item>The visible box of the copy is written onto it as its CropBox (flattening rewrites the MediaBox and the
/// CropBox from the page's own entries), and <c>FPDFPage_Flatten</c> with FLAT_PRINT draws every annotation
/// marked for printing into its content (fields not marked for printing are left out, as on paper).</item>
/// </list>
/// <para>
/// A page whose copy cannot be used for any reason has its fields flattened into the page itself instead, as
/// before the copy was introduced. No copy is made for a PDF over the budget, for a page outside the planned
/// ones, or when the copy function fails or is missing from pdfium.dll; a copy is not used when it is not
/// exactly the page's size, lost annotations, draws differently or could not be flattened. Flattening into the
/// page keeps its layers right; a page that also inherits its /Resources loses what it draws through them, which
/// is the documented limitation. Only when that fails too is the page drawn as it is (its fields blank, as
/// before form fields were handled), so preparing a page never makes a print fail.
/// </para>
/// <para>
/// From then on the copy (or the flattened page) is drawn for the page, through the vector path as before,
/// fields included. The copies lie after the last page the document reports, so no page index reaches them:
/// <see cref="PageCount"/> and the page sizes stay those of the PDF. Documents without form fields are never
/// touched. The cost falls on documents with fields only: every planned page is read once more to find the
/// fields, each page with fields is drawn twice at low resolution for the comparison and read once more, and the
/// copies hold one copy of the objects those pages refer to (their raw data, at most what the PDF file holds)
/// until the document is closed. One thing is still repeated per page: an attribute a page inherits as a direct
/// value (a /Resources dictionary written inline on the /Pages node, for example) is cloned onto every copy, so
/// its parsed size counts once per page with fields (see <see cref="DefaultCopyBudgetBytes"/>, which does not
/// bound it); such dictionaries normally hold only a few references. XFA-only forms have no fields of this kind
/// and still print only their page content; the bundled PDFium is built without XFA.
/// </para>
/// <para>
/// KNOWN LIMITATION (three or more form pages that share one indirect /Contents array). The copies, made in one
/// import call, share the cloned content array, and PDFium's flatten edits that array in place and names its
/// drawing after the first free /FFTn in the shared /XObject dictionary. So the second page's copy also draws
/// the first page's field. The pixel comparison catches that first contaminated copy (when the fields leave ink)
/// and the page takes the in-place fallback, but that flattens the page's ORIGINAL content array, which is
/// shared with the other original pages: from the third page on, the original and the copy differ as well, so
/// those pages also go in place and keep the drawing of the second page. The result is that later pages also
/// print the fields of earlier pages (page 3 shows page 2's field, page 4 shows the fields of pages 2 and 3),
/// with status Ok. It needs a PDF whose form pages share one indirect /Contents array and one indirect
/// /Resources dictionary, which is rare. Fixing it needs private objects per copy (one import call per page),
/// which brings back the memory growth the single call avoids, and PDFium has no call to unshare the array. It is
/// pinned by a characterization test (the one with Known_limitation in its name in FormFieldRenderingTests):
/// whoever fixes this must change that test and the limitation text in the documentation.
/// </para>
/// </remarks>
internal sealed class PdfiumDocument : IPdfDocument
{
    /// <summary>A page whose form fields have not been looked at yet (see <see cref="_drawnFrom"/>).</summary>
    private const int NotPrepared = -1;

    /// <summary>The index <see cref="TryPrepareForPrinting"/> returns when the loaded page is drawn as it is.</summary>
    private const int DrawAsItIs = -1;

    /// <summary>The copy index of a page that has no copy (see <see cref="_copyOf"/>).</summary>
    private const int NoCopy = -1;

    /// <summary>The resolution at which a page and its copy are drawn to check that they look the same.</summary>
    private const int ComparisonDpi = 72;

    /// <summary>The most pixels either comparison drawing may have; larger pages are compared at a lower resolution.</summary>
    internal const int ComparisonMaxPixels = 1_000_000;

    /// <summary>The most pixels either side of a comparison drawing may have, whatever the shape of the page.</summary>
    internal const int ComparisonMaxSide = 4096;

    /// <summary>The most bytes one comparison drawing may take (four per pixel, within the pixel budget).</summary>
    private const long ComparisonMaxBytes = 4L * ComparisonMaxPixels;

    /// <summary>
    /// The copy budget: the largest PDF, in bytes (64 MB), whose pages with form fields are copied before their
    /// fields are flattened. A larger PDF has the fields of every page flattened into the page itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PDFium cannot say how much its page copy takes, so the size of the PDF stands in for it. What it bounds is
    /// the copy of the objects the copied pages refer to (fonts, images, content streams, annotations): the one
    /// copy holds their raw, still compressed data once, which cannot be more than the file holds. The limit
    /// applies per document, so concurrent prints each have their own; forms and labels are almost always far
    /// smaller than 64 MB. Going over it costs little: flattening into the page itself is exactly right for a page
    /// that carries its own resources, and only a page that inherits them from the page tree loses what it draws
    /// through them.
    /// </para>
    /// <para>
    /// What it does not bound: a value a page inherits directly (written inline on a /Pages node rather than as a
    /// separate object, typically a /Resources dictionary) is cloned onto the copy of every page with fields, in
    /// PDFium's parsed form, which takes far more memory than its text. Normally such a dictionary holds only a
    /// few references and costs a few kilobytes per page. A deliberately inflated one does not: measured with the
    /// bundled build on x64, an inline array of 100,000 numbers (195 KB of text) inherited by 100 pages with
    /// fields, in a 1.2 MB PDF, grew private memory by about 600 MB while the pages were drawn.
    /// </para>
    /// </remarks>
    internal const long DefaultCopyBudgetBytes = 64L * 1024 * 1024;

    private readonly PdfiumDocumentHandle _handle;
    private readonly PageFlattener _flattenPage;
    private readonly PageCopier _copyPage;

    // For each page the caller can see, which page of PDFium's document is drawn for it: NotPrepared until the
    // page is first drawn, then the page itself, or the index of its printable copy when it has form fields.
    // Read and written only while holding PdfiumLibrary.Sync, so the work is done once per page however often
    // and from however many threads the page is drawn (copies, the bitmap fallback).
    private readonly int[] _drawnFrom;

    // For each page, the index of its unflattened copy once the one copy of the pages with fields has been made,
    // or NoCopy. Read and written only while holding PdfiumLibrary.Sync.
    private readonly int[] _copyOf;

    // For each page, whether the form-fill environment has already built its missing appearances. Read and
    // written only while holding PdfiumLibrary.Sync.
    private readonly bool[] _appearancesBuilt;

    // The size of the PDF, which stands in for the cost of the copy (see DefaultCopyBudgetBytes).
    private readonly long _sourceLength;

    // The pages PlanToDraw named (null: every page), and whether the one copy has been attempted. Read and
    // written only while holding PdfiumLibrary.Sync.
    private int[]? _plannedPages;
    private bool _copyAttempted;

    // What became of the one copy, for the reason a page without a copy reports. Read and written only while
    // holding PdfiumLibrary.Sync.
    private CopyOutcome _copyOutcome = CopyOutcome.NotAttempted;

    /// <summary>
    /// Wraps an open handle. The caller must hold <see cref="PdfiumLibrary.Sync"/>, because the page
    /// count is read from PDFium here, once, instead of on every access.
    /// </summary>
    /// <param name="handle">The open document; this object takes ownership of it.</param>
    /// <param name="flattenPage">How a page's annotations are flattened; <see langword="null"/> uses
    /// PDFium's <c>FPDFPage_Flatten</c>. Tests pass one that fails.</param>
    /// <param name="copyPage">How the pages with form fields are copied within the document, in one call, before
    /// they are flattened; <see langword="null"/> uses PDFium's <c>FPDF_ImportPagesByIndex</c>. Tests pass one that
    /// fails, changes the copies or counts the calls.</param>
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
        _copyOf = new int[_drawnFrom.Length];
        Array.Fill(_copyOf, NoCopy);
        _appearancesBuilt = new bool[_drawnFrom.Length];
        _sourceLength = handle.SourceLength;
    }

    /// <inheritdoc/>
    public int PageCount { get; }

    /// <summary>
    /// Test hook: called with the pixel width and height of every picture drawn to compare a form page with its
    /// copy, just before it is drawn, while the PDFium lock is held. Lets a test check that the comparison never
    /// draws more than its budget, whatever the page size. <see langword="null"/> (the default) in production.
    /// </summary>
    internal Action<int, int>? ComparisonRendered { get; set; }

    /// <summary>
    /// The copy budget of this document in bytes: <see cref="DefaultCopyBudgetBytes"/> unless a test lowers it to
    /// see what happens to a PDF over the budget. Set it before the first page is drawn.
    /// </summary>
    internal long CopyBudgetBytes { get; set; } = DefaultCopyBudgetBytes;

    /// <summary>
    /// Test hook: called once for every page, the first time it is drawn, with how it was prepared and, for a page
    /// with form fields that was not printed from its copy, why. Called while the PDFium lock is held, so it must
    /// not call back into the document. <see langword="null"/> (the default) in production.
    /// </summary>
    internal Action<int, PagePreparation, CopyRejection>? PagePrepared { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Only the first call before the pages with form fields are prepared counts: those pages are found among
    /// the planned ones (instead of among every page of the PDF) and copied together. Nothing is read from the PDF
    /// here, so a document without form fields costs nothing.
    /// </remarks>
    public void PlanToDraw(IReadOnlyList<int> pageIndices)
    {
        if (pageIndices is null)
        {
            return;
        }

        lock (PdfiumLibrary.Sync)
        {
            if (_copyAttempted || _plannedPages is not null)
            {
                return;
            }

            // In the order given, each page once, and only pages that exist.
            var seen = new HashSet<int>();
            var planned = new List<int>(pageIndices.Count);
            foreach (int index in pageIndices)
            {
                if ((uint)index < (uint)PageCount && seen.Add(index))
                {
                    planned.Add(index);
                }
            }

            _plannedPages = [.. planned];
        }
    }

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
                    (int drawFrom, PagePreparation how, CopyRejection why) = TryPrepareForPrinting(document, pageIndex, page);
                    PagePrepared?.Invoke(pageIndex, how, why);
                    // If this reload fails (the flattened page or copy cannot be loaded again), the unflattened
                    // page loaded above is drawn: its form fields stay blank and the print still reports Ok.
                    // That is accepted: it is practically unreachable, because the same index was loaded
                    // moments ago under the same lock, and a blank-fields page is what printing did before form
                    // fields were handled at all, so preparing a page never makes a print fail.
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
    /// How the page was prepared, why it was not printed from its copy (if it was not), and the page of PDFium's
    /// document to draw for it from now on: the printable copy, or the page itself when its fields were flattened
    /// into it (load it again: the loaded page still holds the old content). For
    /// <see cref="PagePreparation.NoFormFields"/> and <see cref="PagePreparation.DrawnAsItIs"/> the index is
    /// <see cref="DrawAsItIs"/>: the loaded page is drawn as it is.
    /// </returns>
    private (int DrawFrom, PagePreparation How, CopyRejection Why) TryPrepareForPrinting(IntPtr document, int pageIndex, IntPtr page)
    {
        try
        {
            // Only pages that really carry form fields are prepared: every other page is drawn exactly as
            // PDFium draws it. Checked per page, not from the catalog's /AcroForm, because pages copied out of
            // a form by another tool often keep their fields but lose /AcroForm.
            if (!HasFormFields(page))
            {
                return (DrawAsItIs, PagePreparation.NoFormFields, CopyRejection.None);
            }

            IntPtr environment = _handle.GetOrCreateFormFillEnvironment();
            if (environment == IntPtr.Zero)
            {
                // Without the form-fill module, annotations whose appearance PDFium builds only while drawing
                // would be dropped by flattening. Drawing the page as it is loses only the fields.
                return (DrawAsItIs, PagePreparation.DrawnAsItIs, CopyRejection.FormHandlingFailed);
            }

            // The first page with fields makes the one copy of all the planned pages with fields, building their
            // appearances first. A page whose appearances were not built then (it was not planned, or the PDF is
            // over the copy budget) gets them now, before its fields are flattened into the page itself.
            EnsureCopies(document, environment);
            BuildAppearances(pageIndex, page, environment);
        }
        catch (Exception)
        {
            // Preparing a page is an improvement, never a requirement: if even the check for fields fails (for
            // example a pdfium.dll without one of these functions), the page is drawn as it is.
            return (DrawAsItIs, PagePreparation.DrawnAsItIs, CopyRejection.FormHandlingFailed);
        }

        int copyIndex = _copyOf[pageIndex];
        CopyRejection why = copyIndex == NoCopy
            ? _copyOutcome switch
            {
                CopyOutcome.OverBudget => CopyRejection.OverBudget,
                CopyOutcome.Failed => CopyRejection.CopyFailed,

                // The one copy was made (or found no pages with fields to copy) without this page.
                _ => CopyRejection.NotPlanned,
            }
            : TryFlattenCopy(document, pageIndex, page, copyIndex);
        if (why == CopyRejection.None)
        {
            return (copyIndex, PagePreparation.Copy, CopyRejection.None);
        }

        // There is no copy (over the budget, a page that was not planned, a copy that failed or a pdfium.dll
        // without the copy function), or it cannot be used (the known cause is optional content, see the class
        // remarks; others are a copy that lost annotations, a copy of another size or a failed flatten).
        // Flatten the fields into the page itself instead, as before the copy was introduced: the fields print,
        // the layers stay right, and only a page that also inherits its /Resources loses what it draws through
        // them. Drawing the page as it is, with its fields blank, is left for a page whose fields cannot be
        // flattened at all.
        return TryFlattenInPlace(page) ? (pageIndex, PagePreparation.InPlace, why) : (DrawAsItIs, PagePreparation.DrawnAsItIs, why);
    }

    /// <summary>
    /// Makes, at most once per document, the one copy of the planned pages that have form fields: finds them,
    /// builds their missing appearances (which the copies must carry), and copies them all to the end of the
    /// document in one call, so that what they share is copied once. Nothing is copied when the PDF is over the
    /// copy budget. Must be called while holding <see cref="PdfiumLibrary.Sync"/> and a reference on the handle.
    /// Never throws: whatever fails leaves the pages without copies, and they take the fallback.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="environment">The document's form-fill environment.</param>
    private void EnsureCopies(IntPtr document, IntPtr environment)
    {
        if (_copyAttempted)
        {
            return;
        }

        _copyAttempted = true;
        _copyOutcome = CopyOutcome.Failed;
        try
        {
            // Checked before anything is read: over the budget, no page is copied, and nothing needs finding.
            if (_sourceLength > CopyBudgetBytes)
            {
                _copyOutcome = CopyOutcome.OverBudget;
                return;
            }

            var formPages = new List<int>();
            foreach (int index in _plannedPages ?? Enumerable.Range(0, PageCount))
            {
                IntPtr candidate = PdfiumNative.FPDF_LoadPage(document, index);
                if (candidate == IntPtr.Zero)
                {
                    // A page PDFium cannot load is not copied; drawing it reports the damage as before.
                    continue;
                }

                try
                {
                    if (HasFormFields(candidate))
                    {
                        BuildAppearances(index, candidate, environment);
                        formPages.Add(index);
                    }
                }
                finally
                {
                    PdfiumNative.FPDF_ClosePage(candidate);
                }
            }

            if (formPages.Count == 0)
            {
                _copyOutcome = CopyOutcome.NoPagesWithFields;
                return;
            }

            // The copies go after every page there is, so none can take the place of a page the caller sees
            // (PageCount was read when the document was opened and does not change). One call, one object map
            // inside PDFium: an object several of these pages refer to is copied once and shared by their copies.
            int firstCopy = PdfiumNative.FPDF_GetPageCount(document);
            int[] pages = [.. formPages];
            if (!_copyPage(document, pages, firstCopy)
                || PdfiumNative.FPDF_GetPageCount(document) != firstCopy + pages.Length)
            {
                return;
            }

            for (int k = 0; k < pages.Length; k++)
            {
                _copyOf[pages[k]] = firstCopy + k;
            }

            _copyOutcome = CopyOutcome.Made;
        }
        catch (Exception)
        {
            // For example a pdfium.dll without FPDF_ImportPagesByIndex (an experimental PDFium function): every
            // page with fields then has them flattened into the page itself.
        }
    }

    /// <summary>
    /// Has the form-fill environment build the appearances of the page's annotations that are missing from the
    /// file, once per page, and stores them in the annotations (showing the page to the module and taking it away
    /// again; nothing else of the module is used). Must be called while holding <see cref="PdfiumLibrary.Sync"/>.
    /// </summary>
    /// <param name="pageIndex">The page.</param>
    /// <param name="page">The page, loaded.</param>
    /// <param name="environment">The document's form-fill environment.</param>
    private void BuildAppearances(int pageIndex, IntPtr page, IntPtr environment)
    {
        if (_appearancesBuilt[pageIndex])
        {
            return;
        }

        PdfiumNative.FORM_OnAfterLoadPage(page, environment);
        PdfiumNative.FORM_OnBeforeClosePage(page, environment);
        _appearancesBuilt[pageIndex] = true;
    }

    /// <summary>
    /// Checks that the page's copy can stand in for it, and flattens the copy's annotations for printing. Must be
    /// called while holding <see cref="PdfiumLibrary.Sync"/> and a reference on the handle. Never throws: every
    /// failure means "the copy is not used".
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="pageIndex">The page.</param>
    /// <param name="page">The page, loaded (it serves the comparison and the annotation count).</param>
    /// <param name="copyIndex">Where the page's unflattened copy is.</param>
    /// <returns><see cref="CopyRejection.None"/> when the copy was flattened and can be drawn in place of the page;
    /// otherwise why it cannot.</returns>
    private CopyRejection TryFlattenCopy(IntPtr document, int pageIndex, IntPtr page, int copyIndex)
    {
        try
        {
            if (!HaveSameSize(document, pageIndex, copyIndex))
            {
                return CopyRejection.SizeDiffers;
            }

            IntPtr copy = PdfiumNative.FPDF_LoadPage(document, copyIndex);
            if (copy == IntPtr.Zero)
            {
                return CopyRejection.CopyNotLoaded;
            }

            int flattened;
            try
            {
                // PDFium's page copy drops a whole /Annots array when one of its entries cannot be resolved (a
                // damaged reference), leaving a copy without any fields. The pixel comparison below cannot see
                // that, because it draws without the fields, so the annotations are counted first: a copy that
                // does not carry every annotation of the page is not used.
                if (PdfiumNative.FPDFPage_GetAnnotCount(copy) != PdfiumNative.FPDFPage_GetAnnotCount(page))
                {
                    return CopyRejection.AnnotationsLost;
                }

                if (!CopyDrawsLikeThePage(document, page, pageIndex, copy))
                {
                    return CopyRejection.LooksDifferent;
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
            return flattened == PdfiumNative.FlattenSuccess && HaveSameSize(document, pageIndex, copyIndex)
                ? CopyRejection.None
                : CopyRejection.FlattenFailed;
        }
        catch (Exception)
        {
            // Any failure on the copy path only means the copy is not used. It most likely came from flattening
            // (the comparison catches its own failures), so it is reported as such.
            return CopyRejection.FlattenFailed;
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
    /// content is the known case. Comparing the two drawings catches such a difference that shows when the page is
    /// compared, without having to know its cause, and costs two small renders, once, for pages with form fields
    /// only. It does not catch everything: see the known limitation about pages that share one content array in
    /// the remarks of the class.
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
    /// Copies pages within their own document with one PDFium page import, the production <see cref="PageCopier"/>.
    /// The import writes each page's inherited attributes onto its copy and gives the copies their own copies of
    /// the objects the pages use, so what flattening changes in a copy (its resources, the appearance streams of
    /// its annotations) is not shared with the pages themselves. Within the one call PDFium keeps one map from
    /// original to copied objects, so an object several pages share is copied once and shared by their copies.
    /// </summary>
    private static bool CopyWithinDocument(IntPtr document, int[] pageIndices, int insertAt) =>
        PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;

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

    /// <summary>What became of the one copy of a document's pages with form fields.</summary>
    private enum CopyOutcome
    {
        /// <summary>No page with fields has been drawn yet.</summary>
        NotAttempted,

        /// <summary>The copies were made.</summary>
        Made,

        /// <summary>None of the planned pages has form fields, so nothing was copied.</summary>
        NoPagesWithFields,

        /// <summary>The PDF is larger than the copy budget, so nothing was copied.</summary>
        OverBudget,

        /// <summary>Copying failed (or threw), so nothing usable was copied.</summary>
        Failed,
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if ((uint)pageIndex >= (uint)PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {PageCount} page(s); page indexes start at 0.");
        }
    }
}
