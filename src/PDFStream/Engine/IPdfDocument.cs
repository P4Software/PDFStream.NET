using System.Drawing;

namespace PDFStream.Engine;

/// <summary>
/// An open PDF document. Page indexes are zero-based. Dispose it as soon as it is no longer
/// needed: it holds native memory (the engine's copy of the file and the parsed document).
/// </summary>
/// <remarks>
/// Every member may be called from any thread. Implementations backed by a non-thread-safe
/// engine serialise the calls internally, so concurrent callers wait rather than fail.
/// </remarks>
internal interface IPdfDocument : IDisposable
{
    /// <summary>Number of pages in the document (zero or more).</summary>
    int PageCount { get; }

    /// <summary>
    /// Gets the size of a page in points (1/72 inch), as the page is displayed: if the PDF
    /// rotates the page by 90 or 270 degrees, width and height are already swapped.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>The page width and height in points.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of this document.</exception>
    SizeF GetPageSizePoints(int pageIndex);

    /// <summary>
    /// Renders a page straight onto a Windows device context (for example a printer page),
    /// scaled to fill <paramref name="destinationPixels"/>. Vector content stays vector, which
    /// keeps print quality high and spool files small.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="hdc">The device context to draw on. The caller owns it.</param>
    /// <param name="destinationPixels">Where the page goes, in device pixels. The page is
    /// stretched to this rectangle, so the caller keeps the aspect ratio.</param>
    /// <param name="forPrinting"><see langword="true"/> to render as for paper: annotations
    /// marked "do not print" are left out and print-specific appearances are used.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of
    /// this document, or the rectangle has no area.</exception>
    /// <exception cref="ArgumentException"><paramref name="hdc"/> is zero.</exception>
    /// <exception cref="InvalidOperationException">The engine could not load or draw the page,
    /// for example because the device context refused the drawing calls. Callers that print can
    /// fall back to <see cref="RenderToBitmap"/>.</exception>
    void RenderToDeviceContext(int pageIndex, IntPtr hdc, Rectangle destinationPixels, bool forPrinting);

    /// <summary>
    /// Renders a page to a new bitmap on a white background, at the given resolution.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Resolution in dots per inch; the bitmap is the page size in points
    /// times <c>dpi / 72</c>, and carries that resolution.</param>
    /// <returns>A 32-bit ARGB bitmap the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of
    /// this document, or <paramref name="dpi"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The engine could not load or draw the page.</exception>
    Bitmap RenderToBitmap(int pageIndex, int dpi);
}
