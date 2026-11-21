using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PDFStream.Engine;
using PDFStream.Printing;
using PDFStream.Tests.Fixtures;
using PDFStream.Tests.Printing;
using Xunit;

namespace PDFStream.Tests.Engine;

/// <summary>
/// Filled-in PDF form fields must reach the paper. PDFium's two page-drawing calls never draw form-field
/// widgets on their own, so these tests render hand-made form PDFs (see <see cref="FormFixtureFactory"/>)
/// through both of PDFStream's print paths and check the pixels: fields marked for printing are inked,
/// a field without the Print flag is not, and everything the page drew before (its own content and its
/// ordinary annotations) is still drawn, at the same size and in the same place. Pages without form fields
/// must come out exactly as PDFium draws them.
/// </summary>
public sealed class FormFieldRenderingTests
{
    /// <summary>72 dpi makes one pixel one point, so the 400 x 300 point fixture page is 400 x 300 pixels.</summary>
    private const int Dpi = 72;

    /// <summary>
    /// The share of a field's inner rectangle that must be dark for the field to count as drawn. Its stored
    /// appearance is solid black, so a drawn field is (nearly) 100% dark and a missing one 0%.
    /// </summary>
    private const double FullyInked = 0.95;

    private readonly PdfiumEngine _engine = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with xUnit's per-test output sink.</summary>
    public FormFieldRenderingTests(ITestOutputHelper output)
    {
        _output = output;
        _output.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}, runtime: {RuntimeInformation.FrameworkDescription}");
    }

    /// <summary>The two ways PDFStream puts a page on paper.</summary>
    public enum RenderPath
    {
        /// <summary>The bitmap fallback: <see cref="IPdfDocument.RenderToBitmap"/>.</summary>
        Bitmap,

        /// <summary>The vector path printers normally get: <see cref="IPdfDocument.RenderToDeviceContext"/>.</summary>
        DeviceContext,
    }

    /// <summary>Variants of a form page whose layout must not change when its fields are drawn.</summary>
    public static TheoryData<string, RenderPath> LayoutVariants()
    {
        var data = new TheoryData<string, RenderPath>();
        foreach (string variant in new[] { "content and markup", "inherited page attributes", "crop box", "rotate 90", "everything at once" })
        {
            data.Add(variant, RenderPath.Bitmap);
            data.Add(variant, RenderPath.DeviceContext);
        }

        return data;
    }

    /// <summary>Pages without any form field, which must be drawn exactly as PDFium draws them.</summary>
    public static TheoryData<string, RenderPath> FormLessPages()
    {
        var data = new TheoryData<string, RenderPath>();
        foreach (string page in new[] { "text page", "page with content and markup annotations" })
        {
            data.Add(page, RenderPath.Bitmap);
            data.Add(page, RenderPath.DeviceContext);
        }

        return data;
    }

    /// <summary>A filled text field and a ticked checkbox, both marked for printing, are drawn on both paths.</summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Filled_text_field_and_ticked_checkbox_are_drawn(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create());
        using Bitmap page = Render(document, path);

        AssertInked(page, FormFixtureFactory.TextField, "the filled text field");
        AssertInked(page, FormFixtureFactory.CheckBox, "the ticked checkbox");
    }

    /// <summary>A field without the Print flag is a screen-only field and stays off the paper.</summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Field_without_the_print_flag_is_not_drawn(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create());
        using Bitmap page = Render(document, path);

        int dark = Pixels.CountDarkIn(page, FormFixtureFactory.NonPrintingField.ToInnerPixels(Dpi));
        _output.WriteLine($"Dark pixels in the non-printing field: {dark}");
        Assert.Equal(0, dark);
    }

    /// <summary>
    /// Widget annotations on a page whose catalog has no /AcroForm (what page-extraction tools often leave)
    /// are drawn too: the decision is made per page, from the annotations the page really has.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Fields_are_drawn_when_the_catalog_has_no_AcroForm(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { WithAcroForm = false }));
        using Bitmap page = Render(document, path);

        AssertInked(page, FormFixtureFactory.TextField, "the filled text field");
        AssertInked(page, FormFixtureFactory.CheckBox, "the ticked checkbox");
    }

    /// <summary>
    /// A field that holds a value but no stored appearance, in a form that says /NeedAppearances true, is
    /// drawn from its value: PDFium's form engine builds the appearance first.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Field_value_without_a_stored_appearance_is_drawn_when_the_form_asks_for_it(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { NeedAppearances = true }));
        using Bitmap page = Render(document, path);

        // The built appearance is the text HELLO in 24 point Helvetica, so only part of the field is inked.
        int dark = Pixels.CountDarkIn(page, FormFixtureFactory.TextField.ToInnerPixels(Dpi));
        _output.WriteLine($"Dark pixels in the text field built from its value: {dark}");
        Assert.True(dark > 100, $"Expected the field's value to be drawn, found {dark} dark pixels in the field.");
    }

    /// <summary>
    /// A field that holds a value but no stored appearance is drawn from its value even when the form does not
    /// say /NeedAppearances: PDFium's form engine builds the missing appearance.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Field_value_without_a_stored_appearance_is_drawn_even_when_the_form_does_not_ask_for_it(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { TextFieldHasStoredAppearance = false }));
        using Bitmap page = Render(document, path);

        int dark = Pixels.CountDarkIn(page, FormFixtureFactory.TextField.ToInnerPixels(Dpi));
        _output.WriteLine($"Dark pixels in the text field built from its value: {dark}");
        Assert.True(dark > 100, $"Expected the field's value to be drawn, found {dark} dark pixels in the field.");
        AssertInked(page, FormFixtureFactory.CheckBox, "the ticked checkbox");
    }

    /// <summary>
    /// Drawing the fields changes nothing else: the page keeps its size before and after a render, and every
    /// pixel PDFium draws for the page without its fields (page content, ordinary annotations with and without
    /// a stored appearance) is still dark, in the same place, while the fields add their own ink. Covers
    /// inherited page attributes, a crop box and a rotated page, where the boxes are easiest to disturb.
    /// </summary>
    [Theory]
    [MemberData(nameof(LayoutVariants))]
    public void Everything_else_on_a_form_page_is_still_drawn_in_the_same_place(string variant, RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(Variant(variant));
        SizeF expected = RawPdfium.PageSize(pdf);

        using IPdfDocument document = Load(pdf);
        SizeF before = document.GetPageSizePoints(0);
        using Bitmap ours = Render(document, path);
        SizeF after = document.GetPageSizePoints(0);
        using Bitmap reference = path == RenderPath.Bitmap
            ? RawPdfium.RenderBitmap(pdf, ours.Width, ours.Height)
            : RawPdfium.RenderOnDeviceContext(pdf, ours.Width, ours.Height);

        Assert.Equal(expected, before);
        Assert.Equal(expected, after);

        bool[] ourInk = Pixels.DarkMask(ours);
        bool[] referenceInk = Pixels.DarkMask(reference);
        int lost = 0;
        int gained = 0;
        for (int i = 0; i < ourInk.Length; i++)
        {
            lost += referenceInk[i] && !ourInk[i] ? 1 : 0;
            gained += ourInk[i] && !referenceInk[i] ? 1 : 0;
        }

        // Both fields lie inside the crop box, so all of their area must turn up as new ink.
        int fieldArea = (int)((FormFixtureFactory.TextField.Width * FormFixtureFactory.TextField.Height) + (FormFixtureFactory.CheckBox.Width * FormFixtureFactory.CheckBox.Height));
        _output.WriteLine($"{variant}, {path}: page {ours.Width} x {ours.Height}, reference ink {referenceInk.Count(d => d)}, lost {lost}, gained {gained} (field area {fieldArea})");
        Assert.Equal(0, lost);
        Assert.True(gained >= fieldArea * 0.9, $"Expected the fields to add about {fieldArea} dark pixels, found {gained}.");
    }

    /// <summary>A page without any form field is drawn exactly, pixel for pixel, as PDFium alone draws it.</summary>
    [Theory]
    [MemberData(nameof(FormLessPages))]
    public void Form_less_page_is_drawn_exactly_as_PDFium_draws_it(string page, RenderPath path)
    {
        byte[] pdf = page == "text page"
            ? FixtureFactory.CreatePdf(1)
            : FormFixtureFactory.Create(new FormFixtureOptions { WithFields = false, WithPageContent = true, WithMarkupAnnotations = true });

        using IPdfDocument document = Load(pdf);
        using Bitmap ours = Render(document, path);
        using Bitmap reference = path == RenderPath.Bitmap
            ? RawPdfium.RenderBitmap(pdf, ours.Width, ours.Height)
            : RawPdfium.RenderOnDeviceContext(pdf, ours.Width, ours.Height);

        Assert.True(Pixels.CountDark(reference) > 100, "The reference rendering should hold the page's ink.");
        AssertSamePixels(reference, ours);
    }

    /// <summary>Rendering the same form page again (as copies do) gives the same picture, fields included.</summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Rendering_a_form_page_twice_gives_the_same_picture(RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true }));
        using Bitmap first = Render(document, path);
        using Bitmap second = Render(document, path);

        AssertInked(second, FormFixtureFactory.TextField, "the filled text field on the second render");
        AssertSamePixels(first, second);
    }

    /// <summary>A form document that has been rendered closes cleanly, also when disposed twice.</summary>
    [Fact]
    public void Disposing_a_rendered_form_document_is_safe()
    {
        IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { NeedAppearances = true }));
        using (Render(document, RenderPath.Bitmap))
        {
        }

        document.Dispose();
        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.GetPageSizePoints(0));
    }

    /// <summary>
    /// Two threads loading and rendering form documents at the same moment both get their fields drawn: the
    /// form handling runs under the same process-wide lock as every other PDFium call.
    /// </summary>
    [Fact]
    public async Task Two_threads_rendering_form_documents_at_once_both_draw_the_fields()
    {
        const int Rounds = 5;
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { NeedAppearances = true, WithMarkupAnnotations = true });
        using var barrier = new Barrier(2);

        int RenderRepeatedly()
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The second thread never started.");
            }

            int pagesWithFields = 0;
            for (int round = 0; round < Rounds; round++)
            {
                using IPdfDocument document = Load(pdf);
                using Bitmap page = Render(document, RenderPath.Bitmap);
                if (Pixels.CountDarkIn(page, FormFixtureFactory.TextField.ToInnerPixels(Dpi)) > 100)
                {
                    pagesWithFields++;
                }
            }

            return pagesWithFields;
        }

        Task<int> first = Task.Factory.StartNew(RenderRepeatedly, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<int> second = Task.Factory.StartNew(RenderRepeatedly, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        int[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.Equal(Rounds, results[0]);
        Assert.Equal(Rounds, results[1]);
    }

    /// <summary>
    /// A page whose fields cannot be flattened (PDFium reports failure, or the call throws, as a pdfium.dll
    /// without the function would) is drawn exactly as PDFium draws it without any form handling: the fields
    /// stay blank, as before, and nothing else changes.
    /// </summary>
    [Theory]
    [InlineData("reports failure", RenderPath.Bitmap)]
    [InlineData("reports failure", RenderPath.DeviceContext)]
    [InlineData("throws", RenderPath.Bitmap)]
    [InlineData("throws", RenderPath.DeviceContext)]
    public void A_page_that_cannot_be_flattened_is_drawn_as_it_is(string failure, RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true });
        var engine = new PdfiumEngine(FailingFlattener(failure));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (Bitmap ours = Render(document!, path))
        using (Bitmap reference = path == RenderPath.Bitmap
            ? RawPdfium.RenderBitmap(pdf, ours.Width, ours.Height)
            : RawPdfium.RenderOnDeviceContext(pdf, ours.Width, ours.Height))
        {
            AssertInked(ours, FormFixtureFactory.PageMark, "the page's own black square");
            AssertSamePixels(reference, ours);
        }
    }

    /// <summary>A print job over a page that cannot be flattened still succeeds and prints the page.</summary>
    [Theory]
    [InlineData("reports failure")]
    [InlineData("throws")]
    public void A_print_job_whose_page_cannot_be_flattened_still_prints(string failure)
    {
        var engine = new PdfiumEngine(FailingFlattener(failure));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true }), null, out IPdfDocument? document));

        using (document)
        using (var controller = new FakePrintController())
        {
            PrintResult result = PrintJob.Run(document!, [0], TestPrinters.Settings(), "Form", ScaleMode.FitToPage, CancellationToken.None, controller);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(1, result.PagesPrinted);
            Assert.True(Pixels.CountDark(controller.Pages[0]) > 100, "Expected the page content on the sheet.");
        }
    }

    /// <summary>A page without form fields is never flattened, however often it is drawn.</summary>
    [Fact]
    public void A_page_without_form_fields_is_never_flattened()
    {
        int calls = 0;
        var engine = new PdfiumEngine((page, usage) =>
        {
            Interlocked.Increment(ref calls);
            return PdfiumNative.FPDFPage_Flatten(page, usage);
        });
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { WithFields = false, WithPageContent = true, WithMarkupAnnotations = true });
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            Render(document!, RenderPath.Bitmap).Dispose();
            Render(document!, RenderPath.DeviceContext).Dispose();
        }

        Assert.Equal(0, calls);
    }

    /// <summary>A form page is flattened once, for printing, however often and by whichever path it is drawn.</summary>
    [Fact]
    public void A_form_page_is_flattened_once_for_printing()
    {
        var usages = new List<int>();
        var engine = new PdfiumEngine((page, usage) =>
        {
            lock (usages)
            {
                usages.Add(usage);
            }

            return PdfiumNative.FPDFPage_Flatten(page, usage);
        });
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(FormFixtureFactory.Create(), null, out IPdfDocument? document));

        using (document)
        {
            Render(document!, RenderPath.DeviceContext).Dispose();
            Render(document!, RenderPath.Bitmap).Dispose();
            using Bitmap third = Render(document!, RenderPath.DeviceContext);
            AssertInked(third, FormFixtureFactory.TextField, "the filled text field on the third render");
        }

        Assert.Equal([PdfiumNative.FlattenForPrint], usages);
    }

    /// <summary>A flattener that fails the way <paramref name="failure"/> names, without touching the page.</summary>
    private static PageFlattener FailingFlattener(string failure) => failure switch
    {
        "reports failure" => (_, _) => 0,
        "throws" => (_, _) => throw new EntryPointNotFoundException("Unable to find an entry point named 'FPDFPage_Flatten' in DLL 'pdfium'."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
    };

    /// <summary>The fixture options behind each named layout variant.</summary>
    private static FormFixtureOptions Variant(string name) => name switch
    {
        "content and markup" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true },
        "inherited page attributes" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, InheritPageAttributes = true },
        "crop box" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithCropBox = true },
        "rotate 90" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, Rotate = 90 },
        "everything at once" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, InheritPageAttributes = true, WithCropBox = true, Rotate = 90 },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown variant."),
    };

    /// <summary>Opens a PDF with the real engine and fails the test if it does not open.</summary>
    private IPdfDocument Load(byte[] pdf)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));
        return document!;
    }

    /// <summary>
    /// Renders page 1 at <see cref="Dpi"/> through the chosen path: as a bitmap, or onto the device context
    /// of a white bitmap of the page's size (24 bits per pixel, see <see cref="PdfiumEngineTests"/>).
    /// </summary>
    private static Bitmap Render(IPdfDocument document, RenderPath path)
    {
        if (path == RenderPath.Bitmap)
        {
            return document.RenderToBitmap(0, Dpi, quarterTurns: 0);
        }

        SizeF size = document.GetPageSizePoints(0);
        var target = new Bitmap((int)Math.Round(size.Width), (int)Math.Round(size.Height), PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        IntPtr hdc = graphics.GetHdc();
        try
        {
            document.RenderToDeviceContext(0, hdc, new Rectangle(0, 0, target.Width, target.Height), forPrinting: true, quarterTurns: 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return target;
    }

    /// <summary>Fails unless (nearly) the whole inner area of <paramref name="field"/> is dark.</summary>
    private void AssertInked(Bitmap page, PdfRect field, string what)
    {
        Rectangle area = field.ToInnerPixels(Dpi);
        int dark = Pixels.CountDarkIn(page, area);
        int total = area.Width * area.Height;
        _output.WriteLine($"Dark pixels in {what}: {dark} of {total}");
        Assert.True(dark >= total * FullyInked, $"Expected {what} to be drawn: only {dark} of its {total} pixels are dark.");
    }

    /// <summary>Fails unless the two bitmaps have the same size and the same colour in every pixel.</summary>
    private static void AssertSamePixels(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        int[] want = ReadPixels(expected);
        int[] got = ReadPixels(actual);
        int differences = 0;
        int first = -1;
        for (int i = 0; i < want.Length; i++)
        {
            if (want[i] != got[i])
            {
                differences++;
                first = first < 0 ? i : first;
            }
        }

        Assert.True(differences == 0, $"{differences} pixels differ; the first at x {first % expected.Width}, y {first / expected.Width}.");
    }

    /// <summary>Reads every pixel as a 32-bit ARGB value with opaque alpha, so a 24-bit and a 32-bit bitmap compare alike.</summary>
    private static int[] ReadPixels(Bitmap bitmap)
    {
        var pixels = new int[bitmap.Width * bitmap.Height];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * bitmap.Width, bitmap.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] |= unchecked((int)0xFF000000);
        }

        return pixels;
    }
}
