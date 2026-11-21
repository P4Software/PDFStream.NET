using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFStream.Tests.Fixtures;

/// <summary>
/// Builds small PDFs in memory for the tests, so the repository holds no binary fixture files
/// and every test states exactly what kind of PDF it needs.
/// </summary>
/// <remarks>
/// PDFsharp (MIT, pinned to exactly 6.2.4 in the project file; 6.5.0 and later are AGPL-3.0-only)
/// does the writing.
/// It is used only here and never ships with PDFStream.
/// </remarks>
public static class FixtureFactory
{
    /// <summary>US Letter width in points (8.5 inches at 72 points per inch).</summary>
    public const double LetterWidthPoints = 612;

    /// <summary>US Letter height in points (11 inches at 72 points per inch).</summary>
    public const double LetterHeightPoints = 792;

    // PDFsharp keeps process-wide font state. Tests in different classes may run in parallel,
    // so building one fixture at a time keeps the generator out of the picture when a
    // concurrency test fails.
    private static readonly object Gate = new();

    static FixtureFactory()
    {
        // The cross-platform PDFsharp 6 build no longer reads the Windows font folder unless asked
        // to. PDFStream and its tests are Windows-only, so turning it on is the simplest way to get
        // a real font (Arial) embedded in the fixtures.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    /// <summary>
    /// Creates a PDF whose pages each carry the line "Page N of M" in large black text near the
    /// top-left corner, leaving the page margins white.
    /// </summary>
    /// <param name="pages">Number of pages; must be at least 1 (PDFsharp cannot save an empty document).</param>
    /// <param name="userPassword">
    /// Password needed to open the PDF, or <see langword="null"/> for none.
    /// </param>
    /// <param name="ownerPassword">
    /// Password that grants full rights, or <see langword="null"/> for none. Setting only this one
    /// produces an encrypted PDF that still opens without any password.
    /// </param>
    /// <param name="mixedSizes">
    /// When <see langword="true"/>, odd pages (first, third, ...) are A4 portrait and even pages
    /// are US Letter landscape; otherwise every page is A4 portrait.
    /// </param>
    /// <param name="rotation">
    /// A /Rotate value (0, 90, 180 or 270) written on every page after its content is drawn, so the
    /// content itself stays as described above and only the way viewers display the page turns,
    /// clockwise. 90 makes an A4 portrait page display as landscape with its text running down the
    /// right-hand side.
    /// </param>
    /// <returns>The complete PDF file as bytes.</returns>
    public static byte[] CreatePdf(int pages, string? userPassword = null, string? ownerPassword = null, bool mixedSizes = false, int rotation = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);

        lock (Gate)
        {
            using var document = new PdfDocument();
            document.Info.Title = "PDFStream test fixture";
            var font = new XFont("Arial", 36);

            for (int i = 0; i < pages; i++)
            {
                PdfPage page = document.AddPage();
                if (mixedSizes && i % 2 == 1)
                {
                    // Landscape is expressed by the page box itself (wider than tall) rather than
                    // by a /Rotate entry, which is how most generators write landscape pages.
                    page.Width = XUnit.FromPoint(LetterHeightPoints);
                    page.Height = XUnit.FromPoint(LetterWidthPoints);
                }
                else
                {
                    page.Size = PageSize.A4;
                }

                using (XGraphics gfx = XGraphics.FromPdfPage(page))
                {
                    gfx.DrawString($"Page {i + 1} of {pages}", font, XBrushes.Black, new XPoint(72, 144));
                }

                // Set after drawing, so the drawing is not adjusted for it: the content stays where
                // it was written and only the displayed page turns.
                if (rotation != 0)
                {
                    page.Rotate = rotation;
                }
            }

            if (userPassword is not null || ownerPassword is not null)
            {
                if (userPassword is not null)
                {
                    document.SecuritySettings.UserPassword = userPassword;
                }

                if (ownerPassword is not null)
                {
                    document.SecuritySettings.OwnerPassword = ownerPassword;
                }

                // AES-256 is what current PDF tools produce by default, so it is the case that
                // matters most; PDFium also reads the older RC4 and AES-128 schemes.
                document.SecurityHandler.SetEncryptionToV5();
            }

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> that needs <paramref name="userPassword"/> to open, encrypted with
    /// AES-256 by PDFsharp (which reads the file and writes it again; it may restructure the page tree).
    /// </summary>
    /// <param name="pdf">Any PDF PDFsharp can open, for example one from <see cref="FormFixtureFactory"/>.</param>
    /// <param name="userPassword">The password needed to open the result.</param>
    /// <returns>The encrypted PDF as bytes.</returns>
    public static byte[] Encrypt(byte[] pdf, string userPassword)
    {
        lock (Gate)
        {
            using var input = new MemoryStream(pdf);
            using PdfDocument document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            document.SecuritySettings.UserPassword = userPassword;
            document.SecurityHandler.SetEncryptionToV5();

            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }
    }
}
