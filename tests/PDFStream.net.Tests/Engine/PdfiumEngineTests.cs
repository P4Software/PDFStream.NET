using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using PDFStream.net.Engine;
using PDFStream.net.Tests.Fixtures;
using Xunit;

namespace PDFStream.net.Tests.Engine;

/// <summary>
/// Tests <see cref="PdfiumEngine"/> against the real PDFium binary: loading, the three password
/// outcomes, rendering to a bitmap and to a device context, and two threads at once.
/// </summary>
public sealed class PdfiumEngineTests
{
    private const string UserPassword = "open-sesame";
    private const string OwnerPassword = "owner-secret";

    private readonly PdfiumEngine _engine = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with xUnit's per-test output sink.</summary>
    public PdfiumEngineTests(ITestOutputHelper output)
    {
        _output = output;

        // Recorded in every test's output so a run on the x86 test host can be told apart
        // from an x64 run by evidence rather than by assumption.
        _output.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}, runtime: {RuntimeInformation.FrameworkDescription}");
    }

    /// <summary>A valid three-page PDF loads and reports three pages.</summary>
    [Fact]
    public void Loads_valid_pdf_and_reports_page_count()
    {
        byte[] pdf = FixtureFactory.CreatePdf(3);

        PdfLoadStatus status = _engine.TryLoad(pdf, null, out IPdfDocument? document);

        using (document)
        {
            Assert.Equal(PdfLoadStatus.Ok, status);
            Assert.NotNull(document);
            Assert.Equal(3, document.PageCount);
        }
    }

    /// <summary>Bytes that are not a PDF are reported as invalid, with no document.</summary>
    [Fact]
    public void Garbage_bytes_return_Invalid()
    {
        byte[] garbage = Encoding.ASCII.GetBytes("This is plain text pretending to be a PDF file.");

        PdfLoadStatus status = _engine.TryLoad(garbage, null, out IPdfDocument? document);

        Assert.Equal(PdfLoadStatus.Invalid, status);
        Assert.Null(document);
    }

    /// <summary>An empty array is rejected before PDFium is called.</summary>
    [Fact]
    public void Empty_bytes_return_Invalid()
    {
        PdfLoadStatus status = _engine.TryLoad([], null, out IPdfDocument? document);

        Assert.Equal(PdfLoadStatus.Invalid, status);
        Assert.Null(document);
    }

    /// <summary>A PDF that needs a password, opened without one, asks for it.</summary>
    [Fact]
    public void User_password_pdf_without_password_returns_PasswordRequired()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1, UserPassword, OwnerPassword);

        PdfLoadStatus withNull = _engine.TryLoad(pdf, null, out IPdfDocument? first);

        // The first real user passes an empty string, not null, when it has no password,
        // so both must mean "no password given".
        PdfLoadStatus withEmpty = _engine.TryLoad(pdf, string.Empty, out IPdfDocument? second);

        Assert.Equal(PdfLoadStatus.PasswordRequired, withNull);
        Assert.Null(first);
        Assert.Equal(PdfLoadStatus.PasswordRequired, withEmpty);
        Assert.Null(second);
    }

    /// <summary>A PDF that needs a password, opened with the wrong one, says the password is wrong.</summary>
    [Fact]
    public void User_password_pdf_with_wrong_password_returns_PasswordInvalid()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1, UserPassword, OwnerPassword);

        PdfLoadStatus status = _engine.TryLoad(pdf, "not-the-password", out IPdfDocument? document);

        Assert.Equal(PdfLoadStatus.PasswordInvalid, status);
        Assert.Null(document);
    }

    /// <summary>A PDF that needs a password opens with the right one.</summary>
    [Fact]
    public void User_password_pdf_with_right_password_loads()
    {
        byte[] pdf = FixtureFactory.CreatePdf(2, UserPassword, OwnerPassword);

        PdfLoadStatus status = _engine.TryLoad(pdf, UserPassword, out IPdfDocument? document);

        using (document)
        {
            Assert.Equal(PdfLoadStatus.Ok, status);
            Assert.NotNull(document);
            Assert.Equal(2, document.PageCount);
        }
    }

    /// <summary>
    /// A PDF protected only by an owner password (restrictions, no open password) opens without
    /// any password, as every PDF reader does.
    /// </summary>
    [Fact]
    public void Owner_only_password_pdf_loads_without_password()
    {
        byte[] pdf = FixtureFactory.CreatePdf(2, userPassword: null, ownerPassword: OwnerPassword);

        PdfLoadStatus status = _engine.TryLoad(pdf, null, out IPdfDocument? document);

        using (document)
        {
            Assert.Equal(PdfLoadStatus.Ok, status);
            Assert.NotNull(document);
            Assert.Equal(2, document.PageCount);
        }
    }

    /// <summary>
    /// A PDF with only an owner password opens without a password, so an unrelated password the
    /// caller happens to supply must not turn that into a "wrong password" failure.
    /// </summary>
    [Fact]
    public void Owner_only_password_pdf_loads_with_an_unrelated_password()
    {
        byte[] pdf = FixtureFactory.CreatePdf(2, userPassword: null, ownerPassword: OwnerPassword);

        PdfLoadStatus status = _engine.TryLoad(pdf, "not-the-owner-password", out IPdfDocument? document);

        using (document)
        {
            Assert.Equal(PdfLoadStatus.Ok, status);
            Assert.NotNull(document);
            Assert.Equal(2, document.PageCount);
        }
    }

    /// <summary>Disposing a document twice is harmless.</summary>
    [Fact]
    public void Disposing_a_document_twice_is_safe()
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1), null, out IPdfDocument? document));

        document!.Dispose();
        document.Dispose();
    }

    /// <summary>A document that has been disposed refuses further use with ObjectDisposedException.</summary>
    [Fact]
    public void Using_a_document_after_dispose_throws_ObjectDisposedException()
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1), null, out IPdfDocument? document));
        document!.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.GetPageSizePoints(0));
        Assert.Throws<ObjectDisposedException>(() => document.RenderToBitmap(0, 72, 0));
    }

    /// <summary>A page index outside the document is rejected for both page-level calls.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_page_index_throws_ArgumentOutOfRangeException(int pageIndex)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(2), null, out IPdfDocument? document));

        using (document)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => document!.GetPageSizePoints(pageIndex));
            Assert.Throws<ArgumentOutOfRangeException>(() => document!.RenderToBitmap(pageIndex, 72, 0));
        }
    }

    /// <summary>Page sizes come back in points, page by page, as each page is displayed.</summary>
    [Fact]
    public void Page_sizes_are_reported_in_points_for_each_page()
    {
        byte[] pdf = FixtureFactory.CreatePdf(2, mixedSizes: true);
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            SizeF a4Portrait = document!.GetPageSizePoints(0);
            SizeF letterLandscape = document.GetPageSizePoints(1);

            // A4 is 210 x 297 mm, which is 595.28 x 841.89 points.
            Assert.Equal(595.28f, a4Portrait.Width, 0.5f);
            Assert.Equal(841.89f, a4Portrait.Height, 0.5f);
            Assert.Equal((float)FixtureFactory.LetterHeightPoints, letterLandscape.Width, 0.5f);
            Assert.Equal((float)FixtureFactory.LetterWidthPoints, letterLandscape.Height, 0.5f);
        }
    }

    /// <summary>A page with text renders to a bitmap that has ink on a white background.</summary>
    [Fact]
    public void RenderToBitmap_of_page_with_text_has_non_white_pixels()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (Bitmap bitmap = document!.RenderToBitmap(0, 72, 0))
        {
            // At 72 dpi one pixel is one point, so an A4 page is about 595 x 842 pixels.
            Assert.InRange(bitmap.Width, 594, 596);
            Assert.InRange(bitmap.Height, 841, 843);

            // The corner holds no text, so it must be paper-white. This proves the bitmap was
            // filled white before rendering; a zeroed (transparent black) buffer would otherwise
            // count as "ink" everywhere and let a blank render pass the check below.
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());

            int darkPixels = Pixels.CountDark(bitmap);
            _output.WriteLine($"Dark pixels at 72 dpi: {darkPixels}");
            Assert.True(darkPixels > 100, $"Expected the text to leave ink on the page, found {darkPixels} dark pixels.");
        }
    }

    /// <summary>
    /// The device-context path, the one printing uses, draws the page. Rendering onto a memory
    /// device context exercises the exact native signature without needing a printer.
    /// </summary>
    [Fact]
    public void RenderToDeviceContext_draws_the_page_onto_a_device_context()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));

        // 24 bits per pixel on purpose: GDI leaves the alpha byte of 32-bit pixels at zero, which
        // would make every pixel GDI touches look transparent instead of black.
        using (document)
        using (var target = new Bitmap(298, 421, PixelFormat.Format24bppRgb))
        {
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.Clear(Color.White);
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    document!.RenderToDeviceContext(0, hdc, new Rectangle(0, 0, target.Width, target.Height), forPrinting: true, quarterTurns: 0);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            int darkPixels = Pixels.CountDark(target);
            _output.WriteLine($"Dark pixels on the device context: {darkPixels}");
            Assert.True(darkPixels > 50, $"Expected the page text on the device context, found {darkPixels} dark pixels.");
        }
    }

    /// <summary>
    /// A page stored with /Rotate 90 reports its size as displayed: the A4 portrait page comes back
    /// as landscape, width and height swapped.
    /// </summary>
    [Fact]
    public void Page_with_Rotate_90_reports_its_displayed_size_with_width_and_height_swapped()
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1, rotation: 90), null, out IPdfDocument? document));

        using (document)
        {
            SizeF size = document!.GetPageSizePoints(0);

            Assert.Equal(841.89f, size.Width, 0.5f);
            Assert.Equal(595.28f, size.Height, 0.5f);
        }
    }

    /// <summary>
    /// Bitmap rendering applies the page's own /Rotate and then the extra clockwise quarter turns.
    /// The fixture's line of text sits near the top-left of the unturned A4 page, so after a net turn
    /// it must be found where that turn puts it, with the bitmap's width and height to match.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(90, 0)]
    [InlineData(90, 1)]
    [InlineData(90, 3)]
    public void RenderToBitmap_applies_the_page_rotation_and_the_extra_quarter_turns(int pageRotation, int quarterTurns)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1, rotation: pageRotation), null, out IPdfDocument? document));

        using (document)
        using (Bitmap bitmap = document!.RenderToBitmap(0, 72, quarterTurns))
        {
            int netTurns = ((pageRotation / 90) + quarterTurns) % 4;
            AssertTextWhereTheTurnPutsIt(bitmap, netTurns);
        }
    }

    /// <summary>The device-context path turns the page by the requested quarter turns too.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RenderToDeviceContext_turns_the_page_by_the_quarter_turns(int quarterTurns)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1), null, out IPdfDocument? document));

        // A landscape-shaped target, half size, for the turned portrait page.
        using (document)
        using (var target = new Bitmap(421, 298, PixelFormat.Format24bppRgb))
        {
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.Clear(Color.White);
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    document!.RenderToDeviceContext(0, hdc, new Rectangle(0, 0, target.Width, target.Height), forPrinting: true, quarterTurns);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            AssertTextWhereTheTurnPutsIt(target, quarterTurns);
        }
    }

    /// <summary>Quarter turns outside 0 to 3 are refused by both render methods.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Quarter_turns_outside_0_to_3_throw_ArgumentOutOfRangeException(int quarterTurns)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(FixtureFactory.CreatePdf(1), null, out IPdfDocument? document));

        using (document)
        using (var target = new Bitmap(10, 10, PixelFormat.Format24bppRgb))
        using (Graphics graphics = Graphics.FromImage(target))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => document!.RenderToBitmap(0, 72, quarterTurns));
            IntPtr hdc = graphics.GetHdc();
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => document!.RenderToDeviceContext(0, hdc, new Rectangle(0, 0, 10, 10), forPrinting: true, quarterTurns));
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
    }

    /// <summary>
    /// Two threads loading and rendering at the same moment both succeed. PDFium is not
    /// thread-safe, so this only passes if the engine serialises every native call.
    /// </summary>
    [Fact]
    public async Task Two_threads_loading_and_rendering_at_once_both_succeed()
    {
        const int Rounds = 5;
        const int PagesPerDocument = 3;
        byte[] pdf = FixtureFactory.CreatePdf(PagesPerDocument);

        // The barrier releases both threads together so their PDFium work really overlaps.
        using var barrier = new Barrier(2);

        int RenderRepeatedly()
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The second thread never started.");
            }

            int pagesWithInk = 0;
            for (int round = 0; round < Rounds; round++)
            {
                PdfLoadStatus status = _engine.TryLoad(pdf, null, out IPdfDocument? document);
                Assert.Equal(PdfLoadStatus.Ok, status);

                using (document)
                {
                    for (int page = 0; page < document!.PageCount; page++)
                    {
                        using Bitmap bitmap = document.RenderToBitmap(page, 60, 0);
                        if (Pixels.CountDark(bitmap) > 0)
                        {
                            pagesWithInk++;
                        }
                    }
                }
            }

            return pagesWithInk;
        }

        // LongRunning gives each worker its own thread, so the barrier cannot wait on a pool thread
        // that never gets scheduled.
        Task<int> first = Task.Factory.StartNew(RenderRepeatedly, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<int> second = Task.Factory.StartNew(RenderRepeatedly, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        int[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.Equal(Rounds * PagesPerDocument, results[0]);
        Assert.Equal(Rounds * PagesPerDocument, results[1]);
    }

    /// <summary>
    /// Checks that the fixture's text ended up where a net clockwise turn puts it. Unturned, the text
    /// is one line near the top-left of a portrait page. A quarter turn clockwise moves it to the
    /// top-right running down (landscape bitmap), a half turn to the lower right upside down
    /// (portrait), three quarters to the lower left running up (landscape).
    /// </summary>
    private static void AssertTextWhereTheTurnPutsIt(Bitmap bitmap, int netTurns)
    {
        Rectangle ink = Pixels.InkBounds(bitmap);
        Assert.False(ink.IsEmpty, "Expected the page's text in the rendering.");
        bool landscape = bitmap.Width > bitmap.Height;
        string where = $"net turns {netTurns}, bitmap {bitmap.Width} x {bitmap.Height}, ink {ink}";

        switch (netTurns)
        {
            case 0:
                Assert.False(landscape, where);
                Assert.True(ink.Width > ink.Height && ink.Left < bitmap.Width / 4 && ink.Top < bitmap.Height / 4, where);
                break;
            case 1:
                Assert.True(landscape, where);
                Assert.True(ink.Height > ink.Width && ink.Left > bitmap.Width * 3 / 4 && ink.Top < bitmap.Height / 2, where);
                break;
            case 2:
                Assert.False(landscape, where);
                Assert.True(ink.Width > ink.Height && ink.Right > bitmap.Width / 2 && ink.Top > bitmap.Height * 3 / 4, where);
                break;
            default:
                Assert.True(landscape, where);
                Assert.True(ink.Height > ink.Width && ink.Right < bitmap.Width / 4 && ink.Bottom > bitmap.Height / 2, where);
                break;
        }
    }
}
