using System.Drawing;
using System.Drawing.Imaging;
using PDFStream.Engine;
using PDFStream.Printing;
using PDFStream.Tests.Fixtures;
using Xunit;

namespace PDFStream.Tests.Printing;

/// <summary>
/// Tests <see cref="PagePainter"/>: the layout math (where a page lands on the sheet, how large, and
/// whether it is turned) as a pure function, and <see cref="PagePainter.Paint"/>, which draws a page
/// through the device context or the bitmap fallback. Sheets are in-memory bitmaps; no printer is used.
/// </summary>
/// <remarks>
/// Printable areas and destinations are in hundredths of an inch, the unit of the Windows print system.
/// Page sizes are in points (1/72 inch).
/// </remarks>
public sealed class PagePainterTests
{
    private const float Tolerance = 0.01f;

    // US Letter with quarter-inch margins, in hundredths of an inch.
    private static readonly RectangleF PortraitArea = new(0, 0, 800, 1050);
    private static readonly RectangleF LandscapeArea = new(0, 0, 1050, 800);

    private static readonly SizeF LetterPortrait = new(612, 792);
    private static readonly SizeF LetterLandscape = new(792, 612);
    private static readonly SizeF A4Portrait = new(595.28f, 841.89f);
    private static readonly SizeF A5Portrait = new(419.53f, 595.28f);
    private static readonly SizeF TabloidLandscape = new(1224, 792);

    private readonly PdfiumEngine _engine = new();

    // ---------------------------------------------------------------- Fit to page

    /// <summary>A page relatively wider than the area is limited by the width and centred vertically.</summary>
    [Fact]
    public void Fit_portrait_page_on_portrait_sheet_fills_width_and_is_centred()
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(600, 700), new RectangleF(0, 0, 800, 1100), ScaleMode.FitToPage, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(0, 83.333f, 800, 933.333f), destination);
    }

    /// <summary>A page relatively taller than the area is limited by the height and centred horizontally.</summary>
    [Fact]
    public void Fit_portrait_page_relatively_taller_than_the_sheet_fills_height_and_is_centred()
    {
        RectangleF destination = PagePainter.ComputeDestination(A4Portrait, PortraitArea, ScaleMode.FitToPage, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(28.785f, 0, 742.43f, 1050), destination);
    }

    /// <summary>
    /// A landscape page on a portrait sheet is turned, because turned it is printed much larger
    /// (800 x 1035 instead of 800 x 618). The destination is the turned page's footprint.
    /// </summary>
    [Fact]
    public void Fit_landscape_page_on_portrait_sheet_rotates()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterLandscape, PortraitArea, ScaleMode.FitToPage, out bool rotate);

        Assert.True(rotate);
        AssertRect(new RectangleF(0, 7.353f, 800, 1035.294f), destination);
    }

    /// <summary>The reverse: a portrait page on a landscape sheet is turned too.</summary>
    [Fact]
    public void Fit_portrait_page_on_landscape_sheet_rotates()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterPortrait, LandscapeArea, ScaleMode.FitToPage, out bool rotate);

        Assert.True(rotate);
        AssertRect(new RectangleF(7.353f, 0, 1035.294f, 800), destination);
    }

    /// <summary>A page with exactly the sheet's proportions fills the sheet, in either orientation.</summary>
    [Fact]
    public void Fit_page_with_the_sheet_proportions_fills_the_sheet_exactly()
    {
        RectangleF portrait = PagePainter.ComputeDestination(LetterPortrait, new RectangleF(0, 0, 850, 1100), ScaleMode.FitToPage, out bool portraitRotated);
        RectangleF landscape = PagePainter.ComputeDestination(LetterLandscape, new RectangleF(0, 0, 1100, 850), ScaleMode.FitToPage, out bool landscapeRotated);

        Assert.False(portraitRotated);
        Assert.False(landscapeRotated);
        AssertRect(new RectangleF(0, 0, 850, 1100), portrait);
        AssertRect(new RectangleF(0, 0, 1100, 850), landscape);
    }

    /// <summary>Turning a square page gains nothing, so it is never turned.</summary>
    [Theory]
    [InlineData(800, 1100, 0, 150)]
    [InlineData(1100, 800, 150, 0)]
    public void Fit_square_page_is_never_rotated(float areaWidth, float areaHeight, float expectedX, float expectedY)
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(500, 500), new RectangleF(0, 0, areaWidth, areaHeight), ScaleMode.FitToPage, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(expectedX, expectedY, 800, 800), destination);
    }

    /// <summary>On a square sheet turning gains nothing either.</summary>
    [Fact]
    public void Fit_landscape_page_on_square_sheet_is_not_rotated()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterLandscape, new RectangleF(0, 0, 1000, 1000), ScaleMode.FitToPage, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(0, 113.636f, 1000, 772.727f), destination);
    }

    /// <summary>A page with the sheet's orientation is never turned, however narrow it is.</summary>
    [Theory]
    [InlineData(100, 1000, 800, 1100, 345, 0, 110, 1100)]
    [InlineData(1000, 100, 1100, 800, 0, 345, 1100, 110)]
    public void Fit_page_with_the_sheet_orientation_is_not_rotated_even_when_narrow(
        float pageWidth, float pageHeight, float areaWidth, float areaHeight, float x, float y, float width, float height)
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(pageWidth, pageHeight), new RectangleF(0, 0, areaWidth, areaHeight), ScaleMode.FitToPage, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(x, y, width, height), destination);
    }

    /// <summary>The destination is placed inside the printable area wherever that area starts.</summary>
    [Fact]
    public void Fit_is_placed_inside_a_printable_area_that_does_not_start_at_zero()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterLandscape, new RectangleF(25, 40, 800, 1050), ScaleMode.FitToPage, out bool rotate);

        Assert.True(rotate);
        AssertRect(new RectangleF(25, 47.353f, 800, 1035.294f), destination);
    }

    /// <summary>Tiny pages are enlarged to fill the area (and turned when that makes them larger).</summary>
    [Fact]
    public void Fit_enlarges_tiny_pages()
    {
        var area = new RectangleF(0, 0, 800, 1100);

        RectangleF square = PagePainter.ComputeDestination(new SizeF(1, 1), area, ScaleMode.FitToPage, out bool squareRotated);
        RectangleF wide = PagePainter.ComputeDestination(new SizeF(2, 1), area, ScaleMode.FitToPage, out bool wideRotated);

        Assert.False(squareRotated);
        AssertRect(new RectangleF(0, 150, 800, 800), square);
        Assert.True(wideRotated);
        AssertRect(new RectangleF(125, 0, 550, 1100), wide);
    }

    /// <summary>Huge pages (200 inches, the largest a PDF page normally is) are shrunk to the area.</summary>
    [Fact]
    public void Fit_shrinks_huge_pages()
    {
        var area = new RectangleF(0, 0, 800, 1100);

        RectangleF square = PagePainter.ComputeDestination(new SizeF(14400, 14400), area, ScaleMode.FitToPage, out bool squareRotated);
        RectangleF wide = PagePainter.ComputeDestination(new SizeF(14400, 7200), area, ScaleMode.FitToPage, out bool wideRotated);

        Assert.False(squareRotated);
        AssertRect(new RectangleF(0, 150, 800, 800), square);
        Assert.True(wideRotated);
        AssertRect(new RectangleF(125, 0, 550, 1100), wide);
    }

    // ---------------------------------------------------------------- Actual size

    /// <summary>
    /// Actual size prints the page at its own size: points become hundredths of an inch (x 100 / 72),
    /// centred on the area, and what does not fit is cut off evenly on each side.
    /// </summary>
    [Fact]
    public void ActualSize_keeps_point_size_without_scaling()
    {
        RectangleF letter = PagePainter.ComputeDestination(LetterPortrait, PortraitArea, ScaleMode.ActualSize, out bool letterRotated);
        RectangleF a4 = PagePainter.ComputeDestination(A4Portrait, PortraitArea, ScaleMode.ActualSize, out bool a4Rotated);

        Assert.False(letterRotated);
        AssertRect(new RectangleF(-25, -25, 850, 1100), letter);
        Assert.False(a4Rotated);
        AssertRect(new RectangleF(-13.389f, -59.646f, 826.778f, 1169.292f), a4);
    }

    /// <summary>
    /// At actual size a landscape page is turned onto a portrait sheet when more of it fits that way
    /// (here 800 x 1050 of it instead of 800 x 850). The size stays the page's own.
    /// </summary>
    [Fact]
    public void ActualSize_rotates_a_landscape_page_when_more_of_it_fits()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterLandscape, PortraitArea, ScaleMode.ActualSize, out bool rotate);

        Assert.True(rotate);
        AssertRect(new RectangleF(-25, -25, 850, 1100), destination);
    }

    /// <summary>The reverse: a portrait page on a landscape sheet at actual size.</summary>
    [Fact]
    public void ActualSize_rotates_a_portrait_page_onto_a_landscape_sheet_when_more_of_it_fits()
    {
        RectangleF destination = PagePainter.ComputeDestination(LetterPortrait, LandscapeArea, ScaleMode.ActualSize, out bool rotate);

        Assert.True(rotate);
        AssertRect(new RectangleF(-25, -25, 1100, 850), destination);
    }

    /// <summary>A small landscape page fits whole either way, so it is printed as it is, centred.</summary>
    [Fact]
    public void ActualSize_does_not_rotate_a_page_that_fits_either_way()
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(288, 216), PortraitArea, ScaleMode.ActualSize, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(200, 375, 400, 300), destination);
    }

    /// <summary>A huge page at actual size is centred and cut off evenly; nothing overflows the arithmetic.</summary>
    [Fact]
    public void ActualSize_huge_page_is_centred_and_cut_off_evenly()
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(14400, 14400), PortraitArea, ScaleMode.ActualSize, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(-9600, -9475, 20000, 20000), destination);
    }

    /// <summary>A tiny page at actual size stays tiny and is centred.</summary>
    [Fact]
    public void ActualSize_tiny_page_is_centred()
    {
        RectangleF destination = PagePainter.ComputeDestination(new SizeF(1, 1), PortraitArea, ScaleMode.ActualSize, out bool rotate);

        Assert.False(rotate);
        AssertRect(new RectangleF(399.306f, 524.306f, 1.389f, 1.389f), destination);
    }

    // ---------------------------------------------------------------- Mixed sizes and rotated pages

    /// <summary>
    /// Each page of a mixed document gets its own size, position and turn, and the result for a page
    /// does not depend on the pages before it (the same answers in reverse order).
    /// </summary>
    [Fact]
    public void Mixed_size_pages_are_each_computed_independently()
    {
        (SizeF Page, RectangleF Expected, bool Rotated)[] pages =
        [
            (A4Portrait, new RectangleF(28.785f, 0, 742.43f, 1050), false),
            (LetterLandscape, new RectangleF(0, 7.353f, 800, 1035.294f), true),
            (A5Portrait, new RectangleF(30.001f, 0, 739.999f, 1050), false),
            (TabloidLandscape, new RectangleF(60.294f, 0, 679.412f, 1050), true),
        ];

        // Enumerable.Reverse by name: with C# 14 spans, array.Reverse() binds to the in-place span method.
        foreach (var order in new[] { pages, Enumerable.Reverse(pages).ToArray() })
        {
            foreach ((SizeF page, RectangleF expected, bool rotated) in order)
            {
                RectangleF destination = PagePainter.ComputeDestination(page, PortraitArea, ScaleMode.FitToPage, out bool rotate);

                Assert.Equal(rotated, rotate);
                AssertRect(expected, destination);
            }
        }
    }

    /// <summary>
    /// A page with /Rotate 90 is laid out by its displayed size: the engine reports the A4 page as
    /// landscape, so on a portrait A4 sheet it is turned back and fills the sheet.
    /// </summary>
    [Fact]
    public void Rotated_page_is_laid_out_by_its_displayed_size()
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1, rotation: 90), null, out IPdfDocument? document));
        using (document)
        {
            SizeF displayed = document!.GetPageSizePoints(0);

            // A portrait sheet of exactly the page's stored size (PDFsharp writes A4 as whole points),
            // in hundredths of an inch: turned back, the page must fill it exactly.
            var portraitSheet = new RectangleF(0, 0, displayed.Height * 100 / 72, displayed.Width * 100 / 72);

            RectangleF destination = PagePainter.ComputeDestination(displayed, portraitSheet, ScaleMode.FitToPage, out bool rotate);

            Assert.True(displayed.Width > displayed.Height, $"Expected a landscape displayed size, got {displayed}.");
            Assert.True(rotate);
            AssertRect(portraitSheet, destination);
        }
    }

    // ---------------------------------------------------------------- Bad input

    /// <summary>A page without a usable size is refused rather than producing NaN or infinite rectangles.</summary>
    [Theory]
    [InlineData(0f, 792f)]
    [InlineData(612f, 0f)]
    [InlineData(-612f, 792f)]
    [InlineData(float.NaN, 792f)]
    [InlineData(float.PositiveInfinity, 792f)]
    public void ComputeDestination_rejects_a_page_without_a_usable_size(float width, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagePainter.ComputeDestination(new SizeF(width, height), PortraitArea, ScaleMode.FitToPage, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PagePainter.ComputeDestination(new SizeF(width, height), PortraitArea, ScaleMode.ActualSize, out _));
    }

    /// <summary>A printable area without a usable size is refused too.</summary>
    [Theory]
    [InlineData(0f, 1050f)]
    [InlineData(800f, -1f)]
    [InlineData(float.NaN, 1050f)]
    public void ComputeDestination_rejects_a_printable_area_without_a_usable_size(float width, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagePainter.ComputeDestination(LetterPortrait, new RectangleF(0, 0, width, height), ScaleMode.FitToPage, out _));
    }

    /// <summary>A scale mode the painter does not know is refused.</summary>
    [Fact]
    public void ComputeDestination_rejects_an_unknown_scale_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagePainter.ComputeDestination(LetterPortrait, PortraitArea, (ScaleMode)42, out _));
    }

    // ---------------------------------------------------------------- Paint

    /// <summary>
    /// Paint hands the engine the destination in device pixels (hundredths of an inch times the
    /// resolution / 100) and asks for a counter-clockwise quarter turn for a landscape page on a
    /// portrait sheet; the page ends up covering the sheet.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    public void Paint_sends_device_pixels_and_a_counter_clockwise_turn_for_a_landscape_page_on_a_portrait_sheet(int dpi)
    {
        var document = new FakePdfDocument(LetterLandscape);
        using var sheet = new Sheet(425, 550, dpi);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, dpi);

        RenderCall call = Assert.Single(document.Calls);
        Assert.Equal(RenderKind.DeviceContext, call.Kind);
        Assert.Equal(new Rectangle(0, 0, 425 * dpi / 100, 550 * dpi / 100), call.DestinationPixels);
        Assert.Equal(3, call.QuarterTurns);
        Assert.Equal(Color.Black.ToArgb(), sheet.Bitmap.GetPixel(sheet.Bitmap.Width / 2, sheet.Bitmap.Height / 2).ToArgb());
    }

    /// <summary>A portrait page on a landscape sheet gets a clockwise quarter turn.</summary>
    [Fact]
    public void Paint_turns_a_portrait_page_clockwise_onto_a_landscape_sheet()
    {
        var document = new FakePdfDocument(LetterPortrait);
        using var sheet = new Sheet(1100, 850, 100);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        RenderCall call = Assert.Single(document.Calls);
        Assert.Equal(new Rectangle(0, 0, 1100, 850), call.DestinationPixels);
        Assert.Equal(1, call.QuarterTurns);
    }

    /// <summary>A page that already matches the sheet's orientation is not turned.</summary>
    [Fact]
    public void Paint_does_not_turn_a_page_with_the_sheet_orientation()
    {
        var document = new FakePdfDocument(A4Portrait);
        using var sheet = new Sheet(800, 1050, 100);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        RenderCall call = Assert.Single(document.Calls);
        Assert.Equal(0, call.QuarterTurns);
        Assert.Equal(new Rectangle(29, 0, 742, 1050), call.DestinationPixels);
    }

    /// <summary>The real engine draws the page's text onto the sheet through the device context, with no fallback.</summary>
    [Fact]
    public void Paint_draws_the_real_page_through_the_device_context()
    {
        using FakePdfDocument document = LoadRecorded(FixtureFactory.CreatePdf(1));
        using var sheet = new Sheet(850, 1100, 100);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        Assert.All(document.Calls, call => Assert.Equal(RenderKind.DeviceContext, call.Kind));
        Assert.True(Pixels.CountDark(sheet.Bitmap) > 50, "Expected the page's text on the sheet.");
    }

    /// <summary>
    /// End to end with the real engine: a landscape page printed on a portrait sheet comes out turned.
    /// Its line of text, near the top-left of the landscape page, ends up running up the left-hand side
    /// of the sheet (a counter-clockwise turn), so the ink is taller than wide and in the lower left.
    /// </summary>
    [Fact]
    public void Paint_turns_a_real_landscape_page_onto_a_portrait_sheet()
    {
        using FakePdfDocument document = LoadRecorded(FixtureFactory.CreatePdf(2, mixedSizes: true));
        using var sheet = new Sheet(850, 1100, 100);

        PagePainter.Paint(document, 1, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        Rectangle ink = Pixels.InkBounds(sheet.Bitmap);
        Assert.False(ink.IsEmpty, "Expected the page's text on the sheet.");
        Assert.True(ink.Height > ink.Width, $"Expected turned text (taller than wide), got {ink}.");
        Assert.True(ink.Right < sheet.Bitmap.Width / 2, $"Expected the text on the left half, got {ink}.");
        Assert.True(ink.Top > sheet.Bitmap.Height / 2, $"Expected the text on the lower half, got {ink}.");
    }

    /// <summary>
    /// End to end with the real engine: a portrait page stored with /Rotate 90 displays as landscape;
    /// on a portrait sheet it is turned back, so its text reads normally again, across the top-left.
    /// </summary>
    [Fact]
    public void Paint_turns_a_real_rotated_page_back_upright_on_a_portrait_sheet()
    {
        using FakePdfDocument document = LoadRecorded(FixtureFactory.CreatePdf(1, rotation: 90));
        using var sheet = new Sheet(827, 1169, 100);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        Rectangle ink = Pixels.InkBounds(sheet.Bitmap);
        Assert.False(ink.IsEmpty, "Expected the page's text on the sheet.");
        Assert.True(ink.Width > ink.Height, $"Expected upright text (wider than tall), got {ink}.");
        Assert.True(ink.Left < sheet.Bitmap.Width / 4, $"Expected the text to start near the left edge, got {ink}.");
        Assert.True(ink.Top < sheet.Bitmap.Height / 4, $"Expected the text near the top, got {ink}.");
    }

    /// <summary>
    /// When the device context refuses the drawing, the real page is rendered to a bitmap instead and
    /// lands in the same place on the sheet.
    /// </summary>
    [Fact]
    public void Paint_falls_back_to_a_bitmap_of_the_real_page_when_the_device_context_refuses()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        using FakePdfDocument direct = LoadRecorded(pdf);
        using FakePdfDocument refusing = LoadRecorded(pdf, refuseDeviceContext: true);
        using var directSheet = new Sheet(850, 1100, 100);
        using var fallbackSheet = new Sheet(850, 1100, 100);

        PagePainter.Paint(direct, 0, directSheet.Graphics, directSheet.PrintableArea, ScaleMode.FitToPage, 100);
        PagePainter.Paint(refusing, 0, fallbackSheet.Graphics, fallbackSheet.PrintableArea, ScaleMode.FitToPage, 100);

        Assert.Equal([RenderKind.DeviceContext, RenderKind.Bitmap], refusing.Calls.Select(call => call.Kind));
        Rectangle expected = Pixels.InkBounds(directSheet.Bitmap);
        Rectangle actual = Pixels.InkBounds(fallbackSheet.Bitmap);
        Assert.False(actual.IsEmpty, "Expected the page's text on the sheet from the bitmap fallback.");
        Assert.InRange(actual.Left, expected.Left - 3, expected.Left + 3);
        Assert.InRange(actual.Top, expected.Top - 3, expected.Top + 3);
        Assert.InRange(actual.Right, expected.Right - 3, expected.Right + 3);
        Assert.InRange(actual.Bottom, expected.Bottom - 3, expected.Bottom + 3);
    }

    /// <summary>
    /// The fallback bitmap matches the printer resolution on paper, capped at 300 dpi to bound memory:
    /// the dpi asked of the engine is relative to the page, so a page printed at half its size needs
    /// half the dpi for the same dots on paper, and a page enlarged four times needs four times as many.
    /// </summary>
    [Theory]
    [InlineData(600, 144f, 300)]
    [InlineData(200, 144f, 200)]
    [InlineData(600, 288f, 150)]
    [InlineData(600, 36f, 1200)]
    [InlineData(100, 14400f, 1)]
    public void Bitmap_fallback_matches_the_printer_resolution_capped_at_300_dpi(int printerDpi, float pagePoints, int expectedDpi)
    {
        // A 2 x 2 inch sheet keeps the sheet bitmap small even at 600 dpi.
        var document = new FakePdfDocument(new SizeF(pagePoints, pagePoints)) { RefuseDeviceContext = true };
        using var sheet = new Sheet(200, 200, printerDpi);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, printerDpi);

        RenderCall bitmapCall = Assert.Single(document.Calls, call => call.Kind == RenderKind.Bitmap);
        Assert.Equal(expectedDpi, bitmapCall.Dpi);
        Assert.Equal(Color.Black.ToArgb(), sheet.Bitmap.GetPixel(sheet.Bitmap.Width / 2, sheet.Bitmap.Height / 2).ToArgb());
    }

    /// <summary>The bitmap fallback is turned the same way as the device-context path would have been.</summary>
    [Fact]
    public void Bitmap_fallback_is_turned_like_the_device_context_path()
    {
        var document = new FakePdfDocument(LetterLandscape) { RefuseDeviceContext = true };
        using var sheet = new Sheet(850, 1100, 100);

        PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100);

        Assert.All(document.Calls, call => Assert.Equal(3, call.QuarterTurns));
    }

    /// <summary>
    /// A page too large for a bitmap (the engine or GDI+ cannot allocate it) is reported as such, with
    /// its page number, instead of surfacing as a raw out-of-memory or argument error.
    /// </summary>
    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(ArgumentException))]
    public void Bitmap_fallback_too_large_is_reported_as_PageTooLargeException(Type failure)
    {
        var document = new FakePdfDocument(LetterPortrait, LetterPortrait)
        {
            RefuseDeviceContext = true,
            BitmapFailure = _ => (Exception)Activator.CreateInstance(failure)!,
        };
        using var sheet = new Sheet(850, 1100, 100);

        var thrown = Assert.Throws<PagePainter.PageTooLargeException>(
            () => PagePainter.Paint(document, 1, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100));

        Assert.Equal(1, thrown.PageIndex);
        Assert.IsType(failure, thrown.InnerException);
    }

    /// <summary>Errors other than a refused device context are not hidden by the fallback.</summary>
    [Fact]
    public void Paint_lets_a_page_size_failure_through()
    {
        var document = new FakePdfDocument(new SizeF(0, 0));
        using var sheet = new Sheet(850, 1100, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => PagePainter.Paint(document, 0, sheet.Graphics, sheet.PrintableArea, ScaleMode.FitToPage, 100));
        Assert.Empty(document.Calls);
    }

    /// <summary>PrepareSheet switches a sheet to hundredths of an inch and returns its printable area in that unit.</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    public void PrepareSheet_reports_the_area_in_hundredths_of_an_inch(int dpi)
    {
        using var bitmap = new Bitmap(850 * dpi / 100, 1100 * dpi / 100, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(dpi, dpi);
        using Graphics graphics = Graphics.FromImage(bitmap);

        RectangleF area = PagePainter.PrepareSheet(graphics);

        AssertRect(new RectangleF(0, 0, 850, 1100), area, 0.5f);
    }

    private FakePdfDocument LoadRecorded(byte[] pdf, bool refuseDeviceContext = false)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));
        return new FakePdfDocument(document!) { RefuseDeviceContext = refuseDeviceContext };
    }

    private static void AssertRect(RectangleF expected, RectangleF actual, float tolerance = Tolerance)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) <= tolerance
                && Math.Abs(expected.Y - actual.Y) <= tolerance
                && Math.Abs(expected.Width - actual.Width) <= tolerance
                && Math.Abs(expected.Height - actual.Height) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }

    /// <summary>A white in-memory sheet with a Graphics prepared the way a print job prepares a printer page.</summary>
    private sealed class Sheet : IDisposable
    {
        public Sheet(int widthHundredths, int heightHundredths, int dpi)
        {
            // 24 bits per pixel: GDI drawing through the device context leaves 32-bit alpha at zero.
            Bitmap = new Bitmap(widthHundredths * dpi / 100, heightHundredths * dpi / 100, PixelFormat.Format24bppRgb);
            Bitmap.SetResolution(dpi, dpi);
            Graphics = Graphics.FromImage(Bitmap);
            Graphics.Clear(Color.White);
            PrintableArea = PagePainter.PrepareSheet(Graphics);
        }

        public Bitmap Bitmap { get; }

        public Graphics Graphics { get; }

        public RectangleF PrintableArea { get; }

        public void Dispose()
        {
            Graphics.Dispose();
            Bitmap.Dispose();
        }
    }
}
