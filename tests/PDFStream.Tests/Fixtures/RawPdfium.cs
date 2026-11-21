using System.Drawing;
using System.Drawing.Imaging;
using PDFStream.Engine;
using Xunit;

namespace PDFStream.Tests.Fixtures;

/// <summary>
/// Renders page 1 of a PDF straight through PDFium's own drawing calls, with exactly the flags PDFStream
/// prints with and nothing else: no form handling, no flattening, no layout. It is the reference the
/// engine's output is compared with, so a test can show what PDFStream adds (form fields) and prove that it
/// changes nothing else.
/// </summary>
internal static class RawPdfium
{
    /// <summary>The flags PDFStream renders with: annotations, for printing.</summary>
    private const int PrintFlags = PdfiumNative.RenderAnnotations | PdfiumNative.RenderForPrinting;

    /// <summary>The size of page 1 in points, as displayed, from a fresh, untouched copy of the PDF.</summary>
    public static SizeF PageSize(byte[] pdf) => WithFirstPage(pdf, (document, _) =>
    {
        Assert.NotEqual(0, PdfiumNative.FPDF_GetPageSizeByIndexF(document, 0, out PdfiumNative.FsSizeF size));
        return new SizeF(size.Width, size.Height);
    });

    /// <summary>Renders page 1 into a white 32-bit bitmap of the given pixel size with FPDF_RenderPageBitmap.</summary>
    public static Bitmap RenderBitmap(byte[] pdf, int width, int height) => WithFirstPage(pdf, (_, page) =>
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData pixels = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            IntPtr pdfBitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.BitmapFormatBgra, pixels.Scan0, pixels.Stride);
            Assert.NotEqual(IntPtr.Zero, pdfBitmap);
            try
            {
                PdfiumNative.FPDFBitmap_FillRect(pdfBitmap, 0, 0, width, height, PdfiumNative.ColorWhite);
                PdfiumNative.FPDF_RenderPageBitmap(pdfBitmap, page, 0, 0, width, height, 0, PrintFlags);
            }
            finally
            {
                PdfiumNative.FPDFBitmap_Destroy(pdfBitmap);
            }
        }
        finally
        {
            bitmap.UnlockBits(pixels);
        }

        return bitmap;
    });

    /// <summary>
    /// Renders page 1 onto the device context of a white 24-bit bitmap of the given pixel size with
    /// FPDF_RenderPage, the call PDFStream uses on a printer.
    /// </summary>
    public static Bitmap RenderOnDeviceContext(byte[] pdf, int width, int height) => WithFirstPage(pdf, (_, page) =>
    {
        // 24 bits per pixel: GDI leaves the alpha byte of 32-bit pixels at zero (see PdfiumEngineTests).
        var target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        IntPtr hdc = graphics.GetHdc();
        try
        {
            Assert.NotEqual(0, PdfiumNative.FPDF_RenderPage(hdc, page, 0, 0, width, height, 0, PrintFlags));
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return target;
    });

    /// <summary>
    /// Opens a private copy of the PDF, loads page 1 and runs <paramref name="use"/> with the document and
    /// page pointers, all under the process-wide PDFium lock, then closes everything again.
    /// </summary>
    private static T WithFirstPage<T>(byte[] pdf, Func<IntPtr, IntPtr, T> use)
    {
        lock (PdfiumLibrary.Sync)
        {
            PdfiumLibrary.EnsureInitialized();
            using PdfiumDocumentHandle handle = PdfiumDocumentHandle.Open(pdf, null, out int error)
                ?? throw new InvalidOperationException($"PDFium could not open the reference copy (error {error}).");
            IntPtr document = handle.DangerousGetHandle();
            IntPtr page = PdfiumNative.FPDF_LoadPage(document, 0);
            Assert.NotEqual(IntPtr.Zero, page);
            try
            {
                return use(document, page);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }
}
