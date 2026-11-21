using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PDFStream.net.Engine;
using PDFStream.net.Printing;
using PDFStream.net.Tests.Fixtures;
using PDFStream.net.Tests.Printing;
using Xunit;

namespace PDFStream.net.Tests.Engine;

/// <summary>
/// Filled-in PDF form fields must reach the paper. PDFium's two page-drawing calls never draw form-field
/// widgets on their own, so these tests render hand-made form PDFs (see <see cref="FormFixtureFactory"/>)
/// through both of PDFStream.net's print paths and check the pixels: fields marked for printing are inked,
/// a field without the Print flag is not, and everything the page drew before (its own content and its
/// ordinary annotations, and what it draws through resources it inherits from the page tree) is still drawn,
/// at the same size and in the same place. Pages without form fields must come out exactly as PDFium draws
/// them, and in a document with several pages each page index keeps drawing its own page.
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

    /// <summary>The two ways PDFStream.net puts a page on paper.</summary>
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
        foreach (string variant in new[] { "content and markup", "inherited page attributes", "page resources", "inherited resources", "hidden layer", "crop box", "rotate 90", "everything at once" })
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
        foreach (string page in new[] { "text page", "page with content and markup annotations", "page inheriting its resources" })
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
    /// a stored appearance, marks drawn through the page's resources) is still dark, in the same place, while the
    /// fields add their own ink. Covers inherited page attributes and inherited resources, a crop box and a
    /// rotated page, where the boxes and the resources are easiest to disturb.
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
        using Bitmap reference = Reference(pdf, path, ours.Size);

        Assert.Equal(expected, before);
        Assert.Equal(expected, after);

        (int lost, int gained) = CompareInk(reference, ours);

        // Both fields lie inside the crop box, so all of their area must turn up as new ink.
        int fieldArea = FieldArea;
        _output.WriteLine($"{variant}, {path}: page {ours.Width} x {ours.Height}, reference ink {Pixels.CountDark(reference)}, lost {lost}, gained {gained} (field area {fieldArea})");
        Assert.Equal(0, lost);
        AssertFieldsAddTheirAreaAndNothingElse(gained, fieldArea);
    }

    /// <summary>A page without any form field is drawn exactly, pixel for pixel, as PDFium alone draws it.</summary>
    [Theory]
    [MemberData(nameof(FormLessPages))]
    public void Form_less_page_is_drawn_exactly_as_PDFium_draws_it(string page, RenderPath path)
    {
        byte[] pdf = page switch
        {
            "text page" => FixtureFactory.CreatePdf(1),
            "page with content and markup annotations" => FormFixtureFactory.Create(new FormFixtureOptions { WithFields = false, WithPageContent = true, WithMarkupAnnotations = true }),
            "page inheriting its resources" => FormFixtureFactory.Create(new FormFixtureOptions { WithFields = false, WithPageContent = true, WithResourceMarks = true, InheritPageAttributes = true }),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown page."),
        };

        using IPdfDocument document = Load(pdf);
        using Bitmap ours = Render(document, path);
        using Bitmap reference = Reference(pdf, path, ours.Size);

        Assert.True(Pixels.CountDark(reference) > 100, "The reference rendering should hold the page's ink.");
        AssertSamePixels(reference, ours);
    }

    /// <summary>
    /// Marks a form page draws through named entries of its /Resources (a form XObject, an image and a glyph of
    /// a Type3 font) are still drawn next to its fields, whether the page has its own /Resources or inherits
    /// them from the page tree. Flattening the fields into the page itself gave a page that inherits its
    /// resources an empty /Resources of its own, which hid the inherited ones: the XObject and the image
    /// vanished and the glyph fell back to a Helvetica letter, while the print still reported success.
    /// </summary>
    [Theory]
    [InlineData(true, RenderPath.Bitmap)]
    [InlineData(true, RenderPath.DeviceContext)]
    [InlineData(false, RenderPath.Bitmap)]
    [InlineData(false, RenderPath.DeviceContext)]
    public void Marks_drawn_through_the_page_resources_are_drawn_next_to_the_fields(bool inherited, RenderPath path)
    {
        using IPdfDocument document = Load(FormFixtureFactory.Create(new FormFixtureOptions { WithResourceMarks = true, InheritPageAttributes = inherited }));
        using Bitmap page = Render(document, path);

        AssertAllInked(page, ResourceMarksAndFields(inherited ? "inherited" : "own"));
    }

    /// <summary>
    /// A form page with a layer the PDF switches off keeps that layer off the paper while its fields print. A
    /// copy of the page would show the layer (its layer objects are copies, which the document's layer settings
    /// do not name), so this page has its fields flattened into the page itself.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void A_layer_the_pdf_switches_off_stays_off_the_paper_on_a_form_page(RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true, WithResourceMarks = true, WithHiddenLayer = true });
        Rectangle hidden = FormFixtureFactory.HiddenLayerMark.ToInnerPixels(Dpi);

        using IPdfDocument document = Load(pdf);
        using Bitmap ours = Render(document, path);
        using Bitmap reference = Reference(pdf, path, ours.Size);

        Assert.Equal(0, Pixels.CountDarkIn(reference, hidden));
        int darkInHiddenLayer = Pixels.CountDarkIn(ours, hidden);
        _output.WriteLine($"Dark pixels in the switched-off layer's square: {darkInHiddenLayer}");
        Assert.Equal(0, darkInHiddenLayer);
        AssertAllInked(ours, ResourceMarksAndFields("own"));

        (int lost, int gained) = CompareInk(reference, ours);
        _output.WriteLine($"{path}: lost {lost}, gained {gained}");
        Assert.Equal(0, lost);
        AssertFieldsAddTheirAreaAndNothingElse(gained, FieldArea);
    }

    /// <summary>
    /// A password-protected (AES-256) form PDF prints its fields and everything else on the page, the same as
    /// plain PDFium draws the page plus the fields. PDFsharp, which encrypts the fixture, writes the inherited
    /// attributes onto the page (and adds a transparency group), so this covers the copy of an encrypted page,
    /// not inheritance.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void A_password_protected_form_page_prints_its_fields_and_everything_else(RenderPath path)
    {
        const string Password = "secret";
        byte[] pdf = FixtureFactory.Encrypt(FormFixtureFactory.Create(Variant("inherited resources")), Password);
        Assert.Equal(PdfLoadStatus.PasswordRequired, _engine.TryLoad(pdf, null, out _));
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, Password, out IPdfDocument? document));

        using (document)
        using (Bitmap ours = Render(document!, path))
        using (Bitmap reference = path == RenderPath.Bitmap
            ? RawPdfium.RenderBitmap(pdf, ours.Width, ours.Height, password: Password)
            : RawPdfium.RenderOnDeviceContext(pdf, ours.Width, ours.Height, password: Password))
        {
            AssertAllInked(ours, ResourceMarksAndFields("encrypted"));
            (int lost, int gained) = CompareInk(reference, ours);
            _output.WriteLine($"{path}: lost {lost}, gained {gained}");
            Assert.Equal(0, lost);
            AssertFieldsAddTheirAreaAndNothingElse(gained, FieldArea);
        }
    }

    /// <summary>
    /// In a document whose second page is a form page (inheriting its attributes and resources) and whose first
    /// page is a plain page of another size, each page index keeps drawing its own page, in any order and again:
    /// the plain page exactly as PDFium draws it, the form page with its fields and everything else it draws.
    /// The page count and the page sizes stay those of the PDF, and no index past the last page becomes usable
    /// once the form page has been drawn.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Each_page_of_a_document_with_a_form_page_draws_its_own_page(RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { WithLeadingPlainPage = true, InheritPageAttributes = true, WithPageContent = true, WithResourceMarks = true });
        SizeF plainSize = RawPdfium.PageSize(pdf, 0);
        SizeF formSize = RawPdfium.PageSize(pdf, 1);
        Assert.NotEqual(plainSize, formSize);

        using IPdfDocument document = Load(pdf);
        Assert.Equal(2, document.PageCount);

        // The form page first, so whatever is done to prepare it is done before the plain page is drawn.
        using Bitmap form = Render(document, path, pageIndex: 1);
        using Bitmap plain = Render(document, path, pageIndex: 0);
        using Bitmap formAgain = Render(document, path, pageIndex: 1);
        using Bitmap plainReference = Reference(pdf, path, plain.Size, pageIndex: 0);
        using Bitmap formReference = Reference(pdf, path, form.Size, pageIndex: 1);

        AssertSamePixels(plainReference, plain);
        AssertInked(plain, FormFixtureFactory.LeadingPageMark.ToInnerPixels(Dpi, FormFixtureFactory.LeadingPageHeightPoints), "the plain page's black square");

        (int lost, int gained) = CompareInk(formReference, form);
        _output.WriteLine($"Form page, {path}: lost {lost}, gained {gained}");
        Assert.Equal(0, lost);
        AssertFieldsAddTheirAreaAndNothingElse(gained, FieldArea);
        AssertAllInked(form, ResourceMarksAndFields("inherited"));
        AssertSamePixels(form, formAgain);

        Assert.Equal(2, document.PageCount);
        Assert.Equal(plainSize, document.GetPageSizePoints(0));
        Assert.Equal(formSize, document.GetPageSizePoints(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetPageSizePoints(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RenderToBitmap(2, Dpi, quarterTurns: 0));
    }

    /// <summary>
    /// One form document drawn from two threads at the same moment, one thread on each print path, starting
    /// before its form page has ever been drawn (so both threads race to prepare it): every picture is the one a
    /// single thread draws, fields and resource marks included.
    /// </summary>
    [Fact]
    public async Task One_form_document_drawn_from_two_threads_at_once_gives_the_same_pictures()
    {
        const int Rounds = 5;
        byte[] pdf = FormFixtureFactory.Create(Variant("inherited resources"));

        // What one thread alone draws, each from a document of its own.
        using IPdfDocument bitmapDocument = Load(pdf);
        using IPdfDocument deviceContextDocument = Load(pdf);
        using Bitmap expectedBitmap = Render(bitmapDocument, RenderPath.Bitmap);
        using Bitmap expectedDeviceContext = Render(deviceContextDocument, RenderPath.DeviceContext);
        AssertAllInked(expectedBitmap, ResourceMarksAndFields("inherited"));
        AssertAllInked(expectedDeviceContext, ResourceMarksAndFields("inherited"));

        using IPdfDocument shared = Load(pdf);
        using var barrier = new Barrier(2);

        int DrawRepeatedly(RenderPath path, Bitmap expected)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The other thread never started.");
            }

            for (int round = 0; round < Rounds; round++)
            {
                using Bitmap page = Render(shared, path);
                AssertSamePixels(expected, page);
            }

            return Rounds;
        }

        Task<int> bitmaps = Task.Factory.StartNew(() => DrawRepeatedly(RenderPath.Bitmap, expectedBitmap), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<int> deviceContexts = Task.Factory.StartNew(() => DrawRepeatedly(RenderPath.DeviceContext, expectedDeviceContext), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        int[] results = await Task.WhenAll(bitmaps, deviceContexts).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.Equal([Rounds, Rounds], results);
    }

    /// <summary>
    /// The size of the pictures drawn to compare a form page with its copy stays within a fixed budget whatever the
    /// page size: neither side above <see cref="PdfiumDocument.ComparisonMaxSide"/> pixels and the area within
    /// <see cref="PdfiumDocument.ComparisonMaxPixels"/>. A page too extreme to compare is not compared at all.
    /// </summary>
    [Theory]
    [InlineData(612f, 792f)]
    [InlineData(400f, 300f)]
    [InlineData(14400f, 14400f)]
    [InlineData(1_000_000f, 1_000_000f)]
    [InlineData(14400f, 100f)]
    [InlineData(100_000_000f, 1f)]
    [InlineData(1f, 100_000_000f)]
    [InlineData(1e12f, 1f)]
    [InlineData(float.MaxValue, float.MaxValue)]
    [InlineData(float.MaxValue, 1f)]
    public void The_comparison_never_draws_more_than_its_budget(float widthPoints, float heightPoints)
    {
        if (PdfiumDocument.TryGetComparisonSize(widthPoints, heightPoints, out int width, out int height))
        {
            _output.WriteLine($"{widthPoints} x {heightPoints} points: compared at {width} x {height} pixels");
            AssertWithinComparisonBudget(width, height);
        }
        else
        {
            _output.WriteLine($"{widthPoints} x {heightPoints} points: not compared");
        }
    }

    /// <summary>An ordinary page is compared at one pixel per point, and a very large one within the budget.</summary>
    [Fact]
    public void Ordinary_and_very_large_pages_are_compared()
    {
        Assert.True(PdfiumDocument.TryGetComparisonSize(612f, 792f, out int width, out int height));
        Assert.Equal((612, 792), (width, height));

        Assert.True(PdfiumDocument.TryGetComparisonSize(1_000_000f, 1_000_000f, out width, out height));
        AssertWithinComparisonBudget(width, height);
        Assert.Equal(width, height);
    }

    /// <summary>
    /// A page size that is not a real size (not a number, infinite, zero or negative), or so small or so long and
    /// thin that a side would be under one pixel, is not compared: the page takes the fallback for a copy that
    /// cannot be verified.
    /// </summary>
    [Theory]
    [InlineData(float.NaN, 300f)]
    [InlineData(400f, float.NaN)]
    [InlineData(float.PositiveInfinity, 300f)]
    [InlineData(400f, float.NegativeInfinity)]
    [InlineData(0f, 300f)]
    [InlineData(400f, 0f)]
    [InlineData(-400f, 300f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(100_000_000f, 1f)]
    [InlineData(1e12f, 1f)]
    public void Pages_too_odd_to_compare_are_not_compared(float widthPoints, float heightPoints)
    {
        Assert.False(PdfiumDocument.TryGetComparisonSize(widthPoints, heightPoints, out _, out _));
    }

    /// <summary>
    /// Pages of extreme sizes and shapes print without an exception, and every picture drawn to compare a form page
    /// with its copy stays within the budget (checked on the sizes handed to the comparison render).
    /// </summary>
    [Theory]
    [InlineData("very wide and thin form page")]
    [InlineData("very large form page")]
    [InlineData("tiny form page")]
    [InlineData("extremely wide form page")]
    [InlineData("very wide and thin page without fields")]
    public void Pages_of_extreme_size_print_and_their_comparison_stays_within_budget(string shape)
    {
        FormFixtureOptions options = shape switch
        {
            "very wide and thin form page" => new FormFixtureOptions { MediaBox = new PdfRect(0, 0, 100_000_000, 1) },
            "very large form page" => new FormFixtureOptions { MediaBox = new PdfRect(0, 0, 1_000_000, 1_000_000) },
            "tiny form page" => new FormFixtureOptions { MediaBox = new PdfRect(0, 0, 0.5f, 0.5f) },
            "extremely wide form page" => new FormFixtureOptions { MediaBox = new PdfRect(0, 0, 1e12f, 1) },
            "very wide and thin page without fields" => new FormFixtureOptions { WithFields = false, WithPageContent = true, MediaBox = new PdfRect(0, 0, 100_000_000, 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape."),
        };

        using IPdfDocument document = Load(FormFixtureFactory.Create(options));
        var sizes = new List<(int Width, int Height)>();
        ((PdfiumDocument)document).ComparisonRendered = (width, height) => sizes.Add((width, height));

        using var controller = new FakePrintController();
        PrintResult result = PrintJob.Run(document, [0], TestPrinters.Settings(), "Extreme page", ScaleMode.FitToPage, CancellationToken.None, controller);

        _output.WriteLine($"{shape}: page {document.GetPageSizePoints(0)}, comparison renders: {string.Join(", ", sizes)}, result {result.Status}");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.PagesPrinted);
        foreach ((int width, int height) in sizes)
        {
            AssertWithinComparisonBudget(width, height);
        }
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
        byte[] pdf = FormFixtureFactory.Create(Variant("inherited resources"));
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

    /// <summary>
    /// A page that cannot be copied (PDFium reports failure, or the call throws, as a pdfium.dll without the
    /// function would) has its fields flattened into the page itself, as before the copy was introduced: the
    /// fields print, and the page is flattened exactly once. A page with its own resources loses nothing. A page
    /// that takes its resources from the page tree loses what it draws through them, which is the documented
    /// limitation of that fallback (an empty /Resources of its own then hides the inherited one).
    /// </summary>
    [Theory]
    [InlineData("reports failure", RenderPath.Bitmap, false)]
    [InlineData("reports failure", RenderPath.DeviceContext, false)]
    [InlineData("throws", RenderPath.Bitmap, false)]
    [InlineData("throws", RenderPath.DeviceContext, false)]
    [InlineData("reports failure", RenderPath.Bitmap, true)]
    [InlineData("reports failure", RenderPath.DeviceContext, true)]
    [InlineData("throws", RenderPath.Bitmap, true)]
    [InlineData("throws", RenderPath.DeviceContext, true)]
    public void A_page_that_cannot_be_copied_has_its_fields_drawn_into_the_page_itself(string failure, RenderPath path, bool inherited)
    {
        byte[] pdf = FormFixtureFactory.Create(Variant(inherited ? "inherited resources" : "page resources"));
        int flattened = 0;
        var engine = new PdfiumEngine(CountingFlattener(() => Interlocked.Increment(ref flattened)), FailingCopier(failure));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (Bitmap ours = Render(document!, path))
        using (Bitmap reference = Reference(pdf, path, ours.Size))
        {
            AssertFieldsDrawnIntoThePageItself(ours, reference, inherited, path);
        }

        Assert.Equal(1, flattened);
    }

    /// <summary>
    /// A copy that does not come out exactly the size of the page is not used (whatever it printed would land
    /// in the wrong place) and is never flattened: the fields are flattened into the page itself instead, once,
    /// with the documented loss for a page that takes its resources from the page tree.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void A_copy_that_is_not_the_size_of_the_page_is_not_used(RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(Variant("inherited resources"));
        int flattened = 0;
        var engine = new PdfiumEngine(
            CountingFlattener(() => Interlocked.Increment(ref flattened)),
            (document, pageIndices, insertAt) =>
            {
                // A real copy, then made 100 points narrower, as a misbehaving copy might be.
                bool copied = PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;
                IntPtr copy = PdfiumNative.FPDF_LoadPage(document, insertAt);
                Assert.NotEqual(IntPtr.Zero, copy);
                PdfiumNative.FPDFPage_SetMediaBox(copy, 0, 0, FormFixtureFactory.PageWidthPoints - 100, FormFixtureFactory.PageHeightPoints);
                PdfiumNative.FPDF_ClosePage(copy);
                return copied;
            });
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (Bitmap ours = Render(document!, path))
        using (Bitmap reference = Reference(pdf, path, ours.Size))
        {
            Assert.Equal(new SizeF(FormFixtureFactory.PageWidthPoints, FormFixtureFactory.PageHeightPoints), document!.GetPageSizePoints(0));
            AssertFieldsDrawnIntoThePageItself(ours, reference, inherited: true, path);
        }

        Assert.Equal(1, flattened);
    }

    /// <summary>
    /// A copy whose fields cannot be flattened (the first flatten, on the copy, fails) is not used: the fields are
    /// flattened into the page itself instead, so they still print.
    /// </summary>
    [Theory]
    [InlineData("reports failure", RenderPath.Bitmap)]
    [InlineData("reports failure", RenderPath.DeviceContext)]
    [InlineData("throws", RenderPath.Bitmap)]
    [InlineData("throws", RenderPath.DeviceContext)]
    public void A_copy_whose_fields_cannot_be_flattened_has_them_drawn_into_the_page_itself(string failure, RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(Variant("page resources"));
        int calls = 0;
        PageFlattener failFirst = FailingFlattener(failure);
        var engine = new PdfiumEngine((page, usage) => Interlocked.Increment(ref calls) == 1 ? failFirst(page, usage) : PdfiumNative.FPDFPage_Flatten(page, usage));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (Bitmap ours = Render(document!, path))
        using (Bitmap reference = Reference(pdf, path, ours.Size))
        {
            AssertFieldsDrawnIntoThePageItself(ours, reference, inherited: false, path);
        }

        Assert.Equal(2, calls);
    }

    /// <summary>
    /// A page whose annotation list also names an object the file does not contain (a damaged reference) still
    /// prints its fields, and everything else on it, on both paths. PDFium's page copy drops the whole annotation
    /// list because of that one entry, so the copy has no fields at all, and drawing it would leave them blank
    /// (the pixel check cannot see this, because it is drawn without the fields). Such a copy must not be used:
    /// the fields are drawn into the page itself.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void A_damaged_reference_in_the_annotation_list_does_not_leave_the_fields_blank(RenderPath path)
    {
        byte[] pdf = FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true, WithDanglingAnnotationReference = true });

        using IPdfDocument document = Load(pdf);
        using Bitmap ours = Render(document, path);
        using Bitmap reference = Reference(pdf, path, ours.Size);

        AssertInked(ours, FormFixtureFactory.TextField, "the filled text field");
        AssertInked(ours, FormFixtureFactory.CheckBox, "the ticked checkbox");
        AssertInked(ours, FormFixtureFactory.PageMark, "the page's own black square");
        (int lost, int gained) = CompareInk(reference, ours);
        _output.WriteLine($"{path}: lost {lost}, gained {gained}");
        Assert.Equal(0, lost);
        AssertFieldsAddTheirAreaAndNothingElse(gained, FieldArea);
    }

    /// <summary>A print job over a page that cannot be copied still succeeds and prints the page.</summary>
    [Theory]
    [InlineData("reports failure")]
    [InlineData("throws")]
    public void A_print_job_whose_page_cannot_be_copied_still_prints(string failure)
    {
        var engine = new PdfiumEngine(flattenPage: null, FailingCopier(failure));
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

    /// <summary>A page without form fields is never copied or flattened, however often it is drawn.</summary>
    [Fact]
    public void A_page_without_form_fields_is_never_flattened()
    {
        int calls = 0;
        var engine = new PdfiumEngine(
            (page, usage) =>
            {
                Interlocked.Increment(ref calls);
                return PdfiumNative.FPDFPage_Flatten(page, usage);
            },
            (document, pageIndices, insertAt) =>
            {
                Interlocked.Increment(ref calls);
                return PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;
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

    /// <summary>
    /// A form page is copied once, to the end of the document, and the copy flattened once, for printing, however
    /// often and by whichever path the page is drawn.
    /// </summary>
    [Fact]
    public void A_form_page_is_flattened_once_for_printing()
    {
        var usages = new List<int>();
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(
            (page, usage) =>
            {
                lock (usages)
                {
                    usages.Add(usage);
                }

                return PdfiumNative.FPDFPage_Flatten(page, usage);
            },
            (document, pageIndices, insertAt) =>
            {
                lock (copies)
                {
                    copies.Add((pageIndices, insertAt));
                }

                return PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;
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
        (int[] pages, int insertAt) = Assert.Single(copies);
        Assert.Equal([0], pages);
        Assert.Equal(1, insertAt);
    }

    /// <summary>
    /// The pages with form fields are copied in one PDFium call, whichever path draws them and in whatever order,
    /// so an object they share (here one resource dictionary with its images, inherited from the page tree or
    /// named by every page) is copied once, not once per page. Copying page by page made memory grow with the
    /// number of form pages times the size of what they share. Every page still shows its image and its field.
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited)]
    [InlineData(SharedResources.Indirect)]
    public void All_form_pages_are_copied_in_one_go(SharedResources resources)
    {
        const int Pages = 12;
        byte[] pdf = SharedResourceFixtureFactory.Create(Pages, images: 2, withFields: true, resources);
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(flattenPage: null, RecordingCopier(copies));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            foreach (int page in new[] { 5, 0, 11, 1, 2, 3, 4, 6, 7, 8, 9, 10, 5 })
            {
                using Bitmap picture = Render(document!, page % 2 == 0 ? RenderPath.Bitmap : RenderPath.DeviceContext, page);
                AssertSharedResourcePage(picture, page);
            }
        }

        (int[] pages, int insertAt) = Assert.Single(copies);
        Assert.Equal(Enumerable.Range(0, Pages), pages);
        Assert.Equal(Pages, insertAt);
    }

    /// <summary>
    /// When the pages share one resource dictionary, each page still draws only its own field, also when a page
    /// is drawn again after the others. Flattening a copy adds its fields to the copy's resources under a new
    /// name (FFT0, FFT1, ...); with copies that share one dictionary every flatten adds to the same one, so a page
    /// that drew another page's name would print that page's fields.
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited, RenderPath.Bitmap)]
    [InlineData(SharedResources.Inherited, RenderPath.DeviceContext)]
    [InlineData(SharedResources.Indirect, RenderPath.Bitmap)]
    [InlineData(SharedResources.Indirect, RenderPath.DeviceContext)]
    public void Each_page_draws_only_its_own_field_when_the_pages_share_their_resources(SharedResources resources, RenderPath path)
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(4, images: 1, withFields: true, resources);

        using IPdfDocument document = Load(pdf);
        using Bitmap first = Render(document, path, pageIndex: 0);
        for (int page = 0; page < 4; page++)
        {
            using Bitmap picture = Render(document, path, page);
            AssertSharedResourcePage(picture, page);
        }

        using Bitmap firstAgain = Render(document, path, pageIndex: 0);
        AssertSamePixels(first, firstAgain);
    }

    /// <summary>
    /// Two pages that share one content array and one resource dictionary each print only their own field, also
    /// when the first is drawn again after the second. Their copies, made in one go, share those objects too, and
    /// flattening the first copy appends its fields to the shared array: without the comparison of the second
    /// page, that page would print the first page's fields as well (with inherited resources, each copy has its
    /// own dictionary, so the appended name finds nothing on the other page).
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited, RenderPath.Bitmap)]
    [InlineData(SharedResources.Inherited, RenderPath.DeviceContext)]
    [InlineData(SharedResources.Indirect, RenderPath.Bitmap)]
    [InlineData(SharedResources.Indirect, RenderPath.DeviceContext)]
    public void Pages_that_share_their_content_array_each_print_only_their_own_field(SharedResources resources, RenderPath path)
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(2, images: 1, withFields: true, resources, sharedContentArray: true);

        using IPdfDocument document = Load(pdf);
        using Bitmap first = Render(document, path, pageIndex: 0);
        AssertSharedResourcePage(first, 0);
        using Bitmap second = Render(document, path, pageIndex: 1);
        AssertSharedResourcePage(second, 1);
        using Bitmap firstAgain = Render(document, path, pageIndex: 0);
        AssertSharedResourcePage(firstAgain, 0);
        AssertSamePixels(first, firstAgain);
    }

    /// <summary>
    /// KNOWN LIMITATION, pinned as it behaves today. This is a characterization test: it records documented
    /// behaviour that a future fix is expected to change, so whoever fixes it must update this test AND the
    /// limitation text in docs/usage.md, docs/troubleshooting.md, SECURITY.md, CHANGELOG.md and the design doc.
    /// When four form pages share one indirect /Contents array and one indirect /Resources dictionary, the
    /// copies share the cloned content array, and PDFium's flatten appends to that array in place. The pixel
    /// comparison rejects the second page's copy (it shows the first page's field), but the fallback then flattens
    /// the original page, whose content array is also shared, so the third page's original and copy differ as well
    /// and it too goes in place and keeps the second page's drawing: later pages also print the fields of earlier
    /// ones. The print still reports Ok and nothing throws. Nothing here depends on timing: the pages are drawn one
    /// after the other and the ink is counted in fixed rectangles.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void Known_limitation_pages_sharing_one_content_array_also_print_the_fields_of_earlier_pages(RenderPath path)
    {
        const int Pages = 4;
        byte[] pdf = SharedResourceFixtureFactory.Create(Pages, images: 1, withFields: true, SharedResources.Indirect, sharedContentArray: true);
        float height = SharedResourceFixtureFactory.PageHeightPoints;
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();

        using IPdfDocument document = Load(pdf);
        ((PdfiumDocument)document).PagePrepared = (page, how, why) => prepared.Add((page, how, why));

        // Drawn in page order, as a print job does. The fields are 80 x 50 points at four different places, so the
        // ink in page q's field rectangle on page p says whether page q's field was drawn on page p.
        for (int page = 0; page < Pages; page++)
        {
            using Bitmap picture = Render(document, path, page);
            AssertInked(picture, SharedResourceFixtureFactory.FieldOn(page).ToInnerPixels(Dpi, height), $"page {page + 1}'s own field");
            for (int other = 0; other < Pages; other++)
            {
                if (other == page)
                {
                    continue;
                }

                Rectangle area = SharedResourceFixtureFactory.FieldOn(other).ToInnerPixels(Dpi, height);
                int dark = Pixels.CountDarkIn(picture, area);
                _output.WriteLine($"Page {page + 1}: {dark} dark pixels where page {other + 1}'s field lies ({area.Width * area.Height} in the rectangle)");

                // Today: pages 1 and 2 are clean; page 3 also shows page 2's field; page 4 also shows pages 2 and 3.
                bool leaks = page >= 2 && other >= 1 && other < page;
                if (leaks)
                {
                    Assert.True(dark >= area.Width * area.Height * FullyInked, $"Documented limitation: page {page + 1} should also show page {other + 1}'s field, but only {dark} pixels are dark. If this was fixed, update this test and the docs.");
                }
                else
                {
                    Assert.True(dark == 0, $"Page {page + 1} has {dark} dark pixels where page {other + 1}'s field lies, which is not the documented behaviour.");
                }
            }
        }

        // The page itself was never given up: every page was flattened, none was drawn without its fields.
        Assert.Equal(Pages, prepared.Count);
        Assert.DoesNotContain(prepared, entry => entry.How == PagePreparation.DrawnAsItIs);

        // And a whole print job over the same kind of PDF still ends with status Ok.
        using IPdfDocument jobDocument = Load(pdf);
        using var controller = new FakePrintController();
        PrintResult result = PrintJob.Run(jobDocument, [0, 1, 2, 3], TestPrinters.Settings(), "Shared content array", ScaleMode.FitToPage, CancellationToken.None, controller);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Pages, result.PagesPrinted);
    }

    /// <summary>
    /// A print job looks for form fields only among the pages it prints, and copies those pages (each once, in the
    /// order of the job) in one go.
    /// </summary>
    [Fact]
    public void A_print_job_copies_only_the_form_pages_it_prints()
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(6, images: 1, withFields: true, SharedResources.Inherited);
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(flattenPage: null, RecordingCopier(copies));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        using (var controller = new FakePrintController())
        {
            PrintResult result = PrintJob.Run(document!, [4, 1, 4], TestPrinters.Settings(), "Planned pages", ScaleMode.FitToPage, CancellationToken.None, controller);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(3, result.PagesPrinted);
        }

        (int[] pages, int insertAt) = Assert.Single(copies);
        Assert.Equal([4, 1], pages);
        Assert.Equal(6, insertAt);
    }

    /// <summary>
    /// A page drawn although it was not among the pages planned for drawing gets no copy of its own (the one copy
    /// of the document has been made); its fields are flattened into the page itself, so they still print.
    /// </summary>
    [Theory]
    [InlineData(RenderPath.Bitmap)]
    [InlineData(RenderPath.DeviceContext)]
    public void A_page_drawn_outside_the_planned_pages_still_prints_its_fields(RenderPath path)
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(3, images: 1, withFields: true, SharedResources.Indirect);
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(flattenPage: null, RecordingCopier(copies));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            document!.PlanToDraw([0]);
            using Bitmap planned = Render(document, path, pageIndex: 0);
            AssertSharedResourcePage(planned, 0);
            using Bitmap unplanned = Render(document, path, pageIndex: 2);
            AssertSharedResourcePage(unplanned, 2);
        }

        (int[] pages, _) = Assert.Single(copies);
        Assert.Equal([0], pages);
    }

    /// <summary>
    /// A PDF larger than the copy budget gets no copy at all: the fields of every page are flattened into the page
    /// itself, and they all print. A page that carries its resources itself loses nothing; a page that inherits
    /// them from the page tree loses its image, which is the documented limitation of that fallback (asserted, so
    /// that a change is noticed). No error is reported.
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited, RenderPath.Bitmap)]
    [InlineData(SharedResources.Inherited, RenderPath.DeviceContext)]
    [InlineData(SharedResources.Indirect, RenderPath.Bitmap)]
    [InlineData(SharedResources.Indirect, RenderPath.DeviceContext)]
    public void A_pdf_over_the_copy_budget_has_its_fields_drawn_into_the_pages_themselves(SharedResources resources, RenderPath path)
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(4, images: 1, withFields: true, resources);
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(flattenPage: null, RecordingCopier(copies));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            ((PdfiumDocument)document!).CopyBudgetBytes = pdf.Length - 1;
            for (int page = 0; page < 4; page++)
            {
                using Bitmap picture = Render(document, path, page);
                AssertSharedResourcePage(picture, page, imageDrawn: resources == SharedResources.Indirect);
            }
        }

        Assert.Empty(copies);
    }

    /// <summary>A PDF exactly the size of the copy budget is still copied, and every page shows its image and field.</summary>
    [Fact]
    public void A_pdf_exactly_at_the_copy_budget_is_copied()
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(4, images: 1, withFields: true, SharedResources.Inherited);
        var copies = new List<(int[] Pages, int InsertAt)>();
        var engine = new PdfiumEngine(flattenPage: null, RecordingCopier(copies));
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));

        using (document)
        {
            ((PdfiumDocument)document!).CopyBudgetBytes = pdf.Length;
            for (int page = 0; page < 4; page++)
            {
                using Bitmap picture = Render(document, RenderPath.Bitmap, page);
                AssertSharedResourcePage(picture, page);
            }
        }

        Assert.Single(copies);
    }

    /// <summary>The copy budget is 64 MB, the number the documentation states.</summary>
    [Fact]
    public void The_copy_budget_is_64_MB()
    {
        Assert.Equal(64L * 1024 * 1024, PdfiumDocument.DefaultCopyBudgetBytes);
        using IPdfDocument document = Load(FormFixtureFactory.Create());
        Assert.Equal(PdfiumDocument.DefaultCopyBudgetBytes, ((PdfiumDocument)document).CopyBudgetBytes);
    }

    /// <summary>The key cases of form-page preparation, each with how its page must be prepared and why.</summary>
    public static TheoryData<string, RenderPath> PreparationCases()
    {
        var data = new TheoryData<string, RenderPath>();
        foreach (string name in new[]
        {
            "plain form page", "inherited resources", "page without fields", "damaged annotation reference", "hidden layer",
            "copy function missing", "copy refused", "copy of another size", "copy cannot be flattened",
            "nothing can be flattened", "over the copy budget",
        })
        {
            data.Add(name, RenderPath.Bitmap);
            data.Add(name, RenderPath.DeviceContext);
        }

        return data;
    }

    /// <summary>
    /// Every key case reports, through the document's preparation hook, which branch its page took and why: the
    /// copy for an ordinary form page and for one that inherits its resources; the page itself (in place) when the
    /// copy is missing, refused, lost annotations, draws differently, has another size or cannot be flattened, or
    /// when the PDF is over the copy budget; drawn as it is only when nothing can be flattened. A PDFium update
    /// that made every copy fail would show here directly, not only as a loss of ink on some pages.
    /// </summary>
    [Theory]
    [MemberData(nameof(PreparationCases))]
    public void Each_key_case_reports_how_its_page_was_prepared(string name, RenderPath path)
    {
        (byte[] pdf, PdfiumEngine engine, PagePreparation expectedHow, CopyRejection expectedWhy, long? budget) = name switch
        {
            "plain form page" => (FormFixtureFactory.Create(), new PdfiumEngine(), PagePreparation.Copy, CopyRejection.None, (long?)null),
            "inherited resources" => (FormFixtureFactory.Create(Variant("inherited resources")), new PdfiumEngine(), PagePreparation.Copy, CopyRejection.None, null),
            "page without fields" => (FormFixtureFactory.Create(new FormFixtureOptions { WithFields = false, WithPageContent = true }), new PdfiumEngine(), PagePreparation.NoFormFields, CopyRejection.None, null),
            "damaged annotation reference" => (FormFixtureFactory.Create(new FormFixtureOptions { WithPageContent = true, WithDanglingAnnotationReference = true }), new PdfiumEngine(), PagePreparation.InPlace, CopyRejection.AnnotationsLost, null),
            "hidden layer" => (FormFixtureFactory.Create(Variant("hidden layer")), new PdfiumEngine(), PagePreparation.InPlace, CopyRejection.LooksDifferent, null),
            "copy function missing" => (FormFixtureFactory.Create(Variant("page resources")), new PdfiumEngine(flattenPage: null, FailingCopier("throws")), PagePreparation.InPlace, CopyRejection.CopyFailed, null),
            "copy refused" => (FormFixtureFactory.Create(Variant("page resources")), new PdfiumEngine(flattenPage: null, FailingCopier("reports failure")), PagePreparation.InPlace, CopyRejection.CopyFailed, null),
            "copy of another size" => (FormFixtureFactory.Create(Variant("page resources")), new PdfiumEngine(flattenPage: null, NarrowingCopier()), PagePreparation.InPlace, CopyRejection.SizeDiffers, null),
            "copy cannot be flattened" => (FormFixtureFactory.Create(Variant("page resources")), new PdfiumEngine(FailingFirstFlattener()), PagePreparation.InPlace, CopyRejection.FlattenFailed, null),
            "nothing can be flattened" => (FormFixtureFactory.Create(Variant("page resources")), new PdfiumEngine(FailingFlattener("reports failure")), PagePreparation.DrawnAsItIs, CopyRejection.FlattenFailed, null),
            "over the copy budget" => (FormFixtureFactory.Create(Variant("inherited resources")), new PdfiumEngine(), PagePreparation.InPlace, CopyRejection.OverBudget, 1L),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown case."),
        };

        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();
        using (document)
        {
            var pdfium = (PdfiumDocument)document!;
            if (budget is long bytes)
            {
                pdfium.CopyBudgetBytes = bytes;
            }

            pdfium.PagePrepared = (page, how, why) => prepared.Add((page, how, why));
            Render(document!, path).Dispose();
            Render(document!, path).Dispose();
        }

        _output.WriteLine($"{name}, {path}: {string.Join(", ", prepared)}");
        Assert.Equal([(0, expectedHow, expectedWhy)], prepared);
    }

    /// <summary>
    /// Many pages sharing one resource dictionary: under the copy budget every page prints from its copy; over
    /// it, every page has its fields flattened into itself and says that the budget was the reason.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_page_of_a_document_reports_the_copy_budget_decision(bool overBudget)
    {
        const int Pages = 6;
        byte[] pdf = SharedResourceFixtureFactory.Create(Pages, images: 1, withFields: true, SharedResources.Inherited);
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();
        using (IPdfDocument document = Load(pdf))
        {
            var pdfium = (PdfiumDocument)document;
            pdfium.CopyBudgetBytes = overBudget ? pdf.Length - 1 : pdf.Length;
            pdfium.PagePrepared = (page, how, why) => prepared.Add((page, how, why));
            for (int page = 0; page < Pages; page++)
            {
                using Bitmap picture = Render(document, RenderPath.Bitmap, page);
                AssertSharedResourcePage(picture, page, imageDrawn: !overBudget);
            }
        }

        (PagePreparation how, CopyRejection why) expected = overBudget ? (PagePreparation.InPlace, CopyRejection.OverBudget) : (PagePreparation.Copy, CopyRejection.None);
        Assert.Equal(Enumerable.Range(0, Pages).Select(page => (page, expected.how, expected.why)), prepared);
    }

    /// <summary>
    /// The pages that share one content array: with inherited resources both print from their copies; with one
    /// shared resource dictionary the second page's copy shows the first page's fields, so the comparison sends it
    /// to the fallback, and that is what it reports.
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited)]
    [InlineData(SharedResources.Indirect)]
    public void Pages_that_share_their_content_array_report_how_they_were_prepared(SharedResources resources)
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(2, images: 1, withFields: true, resources, sharedContentArray: true);
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();
        using (IPdfDocument document = Load(pdf))
        {
            ((PdfiumDocument)document).PagePrepared = (page, how, why) => prepared.Add((page, how, why));
            Render(document, RenderPath.Bitmap, pageIndex: 0).Dispose();
            Render(document, RenderPath.Bitmap, pageIndex: 1).Dispose();
        }

        (PagePreparation, CopyRejection) second = resources == SharedResources.Indirect ? (PagePreparation.InPlace, CopyRejection.LooksDifferent) : (PagePreparation.Copy, CopyRejection.None);
        Assert.Equal([(0, PagePreparation.Copy, CopyRejection.None), (1, second.Item1, second.Item2)], prepared);
    }

    /// <summary>A page drawn outside the planned pages reports that it was not planned.</summary>
    [Fact]
    public void A_page_drawn_outside_the_planned_pages_reports_it()
    {
        byte[] pdf = SharedResourceFixtureFactory.Create(3, images: 1, withFields: true, SharedResources.Indirect);
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();
        using (IPdfDocument document = Load(pdf))
        {
            ((PdfiumDocument)document).PagePrepared = (page, how, why) => prepared.Add((page, how, why));
            document.PlanToDraw([0]);
            Render(document, RenderPath.Bitmap, pageIndex: 0).Dispose();
            Render(document, RenderPath.Bitmap, pageIndex: 2).Dispose();
        }

        Assert.Equal([(0, PagePreparation.Copy, CopyRejection.None), (2, PagePreparation.InPlace, CopyRejection.NotPlanned)], prepared);
    }

    /// <summary>A real copy of the requested pages, each then made 100 points narrower, as a misbehaving copy might be.</summary>
    private static PageCopier NarrowingCopier() => (document, pageIndices, insertAt) =>
    {
        bool copied = PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;
        for (int k = 0; copied && k < pageIndices.Length; k++)
        {
            IntPtr copy = PdfiumNative.FPDF_LoadPage(document, insertAt + k);
            Assert.NotEqual(IntPtr.Zero, copy);
            PdfiumNative.FPDFPage_SetMediaBox(copy, 0, 0, FormFixtureFactory.PageWidthPoints - 100, FormFixtureFactory.PageHeightPoints);
            PdfiumNative.FPDF_ClosePage(copy);
        }

        return copied;
    };

    /// <summary>PDFium's own flattener, except that the first call (the one on the copy) reports failure.</summary>
    private static PageFlattener FailingFirstFlattener()
    {
        int calls = 0;
        return (page, usage) => Interlocked.Increment(ref calls) == 1 ? 0 : PdfiumNative.FPDFPage_Flatten(page, usage);
    }

    /// <summary>PDFium's page copy, recording each call (the pages and where their copies go) in <paramref name="calls"/>.</summary>
    private static PageCopier RecordingCopier(List<(int[] Pages, int InsertAt)> calls) => (document, pageIndices, insertAt) =>
    {
        lock (calls)
        {
            calls.Add(([.. pageIndices], insertAt));
        }

        return PdfiumNative.FPDF_ImportPagesByIndex(document, document, pageIndices, (uint)pageIndices.Length, insertAt) != 0;
    };

    /// <summary>
    /// Fails unless a page of a <see cref="SharedResourceFixtureFactory"/> PDF shows its own field, none of the
    /// other pages' fields, and its image (or, when <paramref name="imageDrawn"/> is <see langword="false"/>, does
    /// not show the image: the documented loss of a page that inherits its resources and has its fields flattened
    /// into itself).
    /// </summary>
    private void AssertSharedResourcePage(Bitmap picture, int page, bool imageDrawn = true)
    {
        float height = SharedResourceFixtureFactory.PageHeightPoints;
        AssertInked(picture, SharedResourceFixtureFactory.FieldOn(page).ToInnerPixels(Dpi, height), $"page {page + 1}'s own field");
        for (int other = 1; other < 4; other++)
        {
            int dark = Pixels.CountDarkIn(picture, SharedResourceFixtureFactory.FieldOn(page + other).ToInnerPixels(Dpi, height));
            Assert.True(dark == 0, $"Page {page + 1} has {dark} dark pixels where another page's field lies: that page's field was drawn on it.");
        }

        Rectangle image = SharedResourceFixtureFactory.ImageMark.ToInnerPixels(Dpi, height);
        if (imageDrawn)
        {
            AssertInked(picture, image, $"page {page + 1}'s image, drawn through the shared resources");
        }
        else
        {
            int dark = Pixels.CountDarkIn(picture, image);
            _output.WriteLine($"Page {page + 1}: {dark} dark pixels where the image drawn through inherited resources lies (documented limitation: expected missing)");
            Assert.True(dark <= image.Width * image.Height * 0.05, $"Expected page {page + 1}'s image to be missing (documented limitation), found {dark} dark pixels.");
        }
    }

    /// <summary>A flattener that fails the way <paramref name="failure"/> names, without touching the page.</summary>
    private static PageFlattener FailingFlattener(string failure) => failure switch
    {
        "reports failure" => (_, _) => 0,
        "throws" => (_, _) => throw new EntryPointNotFoundException("Unable to find an entry point named 'FPDFPage_Flatten' in DLL 'pdfium'."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
    };

    /// <summary>PDFium's own flattener, which also calls <paramref name="counted"/> every time it is used.</summary>
    private static PageFlattener CountingFlattener(Action counted) => (page, usage) =>
    {
        counted();
        return PdfiumNative.FPDFPage_Flatten(page, usage);
    };

    /// <summary>A page copier that fails the way <paramref name="failure"/> names, without copying anything.</summary>
    private static PageCopier FailingCopier(string failure) => failure switch
    {
        "reports failure" => (_, _, _) => false,
        "throws" => (_, _, _) => throw new EntryPointNotFoundException("Unable to find an entry point named 'FPDF_ImportPagesByIndex' in DLL 'pdfium'."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
    };

    /// <summary>The fixture options behind each named layout variant.</summary>
    private static FormFixtureOptions Variant(string name) => name switch
    {
        "content and markup" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true },
        "inherited page attributes" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, InheritPageAttributes = true },
        "page resources" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithResourceMarks = true },
        "inherited resources" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithResourceMarks = true, InheritPageAttributes = true },
        "hidden layer" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithResourceMarks = true, WithHiddenLayer = true },
        "crop box" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithCropBox = true },
        "rotate 90" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, Rotate = 90 },
        "everything at once" => new FormFixtureOptions { WithPageContent = true, WithMarkupAnnotations = true, WithResourceMarks = true, InheritPageAttributes = true, WithCropBox = true, Rotate = 90 },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown variant."),
    };

    /// <summary>Opens a PDF with the real engine and fails the test if it does not open.</summary>
    private IPdfDocument Load(byte[] pdf)
    {
        Assert.Equal(PdfLoadStatus.Ok, _engine.TryLoad(pdf, null, out IPdfDocument? document));
        return document!;
    }

    /// <summary>
    /// Renders a page (page 1 by default) at <see cref="Dpi"/> through the chosen path: as a bitmap, or onto the
    /// device context of a white bitmap of the page's size (24 bits per pixel, see <see cref="PdfiumEngineTests"/>).
    /// </summary>
    private static Bitmap Render(IPdfDocument document, RenderPath path, int pageIndex = 0)
    {
        if (path == RenderPath.Bitmap)
        {
            return document.RenderToBitmap(pageIndex, Dpi, quarterTurns: 0);
        }

        SizeF size = document.GetPageSizePoints(pageIndex);
        var target = new Bitmap((int)Math.Round(size.Width), (int)Math.Round(size.Height), PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        IntPtr hdc = graphics.GetHdc();
        try
        {
            document.RenderToDeviceContext(pageIndex, hdc, new Rectangle(0, 0, target.Width, target.Height), forPrinting: true, quarterTurns: 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return target;
    }

    /// <summary>Fails unless a comparison picture of this size is within the budget the engine promises.</summary>
    private static void AssertWithinComparisonBudget(int width, int height)
    {
        Assert.InRange(width, 1, PdfiumDocument.ComparisonMaxSide);
        Assert.InRange(height, 1, PdfiumDocument.ComparisonMaxSide);
        Assert.True((long)width * height <= PdfiumDocument.ComparisonMaxPixels, $"A {width} x {height} comparison picture is over the pixel budget.");
    }

    /// <summary>The area, in pixels at <see cref="Dpi"/>, of the two printable fields (the text field and the checkbox).</summary>
    private static int FieldArea => (int)((FormFixtureFactory.TextField.Width * FormFixtureFactory.TextField.Height) + (FormFixtureFactory.CheckBox.Width * FormFixtureFactory.CheckBox.Height));

    /// <summary>
    /// Fails unless the new ink is about the area of the fields: at least 90% of it (the fields are drawn) and at
    /// most 1% more (nothing else is drawn that plain PDFium does not draw, such as a layer the PDF switches off;
    /// the margin covers anti-aliased field edges).
    /// </summary>
    private static void AssertFieldsAddTheirAreaAndNothingElse(int gained, int fieldArea)
    {
        Assert.True(gained >= fieldArea * 0.9, $"Expected the fields to add about {fieldArea} dark pixels, found {gained}.");
        Assert.True(gained <= fieldArea * 1.01, $"Expected the fields to add about {fieldArea} dark pixels and nothing else, found {gained}: something not drawn by PDFium alone was added.");
    }

    /// <summary>The reference picture of a page: plain PDFium on the same path, at the same pixel size.</summary>
    private static Bitmap Reference(byte[] pdf, RenderPath path, Size size, int pageIndex = 0) => path == RenderPath.Bitmap
        ? RawPdfium.RenderBitmap(pdf, size.Width, size.Height, pageIndex)
        : RawPdfium.RenderOnDeviceContext(pdf, size.Width, size.Height, pageIndex);

    /// <summary>
    /// Compares the ink of two pictures of the same size pixel by pixel: how many pixels dark in
    /// <paramref name="reference"/> are not dark in <paramref name="ours"/> (lost), and the other way round (gained).
    /// </summary>
    private static (int Lost, int Gained) CompareInk(Bitmap reference, Bitmap ours)
    {
        Assert.Equal(reference.Size, ours.Size);
        bool[] ourInk = Pixels.DarkMask(ours);
        bool[] referenceInk = Pixels.DarkMask(reference);
        int lost = 0;
        int gained = 0;
        for (int i = 0; i < ourInk.Length; i++)
        {
            lost += referenceInk[i] && !ourInk[i] ? 1 : 0;
            gained += ourInk[i] && !referenceInk[i] ? 1 : 0;
        }

        return (lost, gained);
    }

    /// <summary>The three resource marks and the two printable fields of an unrotated, uncropped fixture page.</summary>
    /// <param name="resources">How the page gets its resources ("own" or "inherited"), for the failure messages.</param>
    private static (PdfRect Area, string What)[] ResourceMarksAndFields(string resources) =>
    [
        (FormFixtureFactory.ResourceXObject, $"the form XObject drawn through the page's {resources} resources"),
        (FormFixtureFactory.ResourceImage, $"the image drawn through the page's {resources} resources"),
        (FormFixtureFactory.ResourceFontGlyph, $"the Type3 glyph drawn through the page's {resources} resources"),
        (FormFixtureFactory.TextField, "the filled text field"),
        (FormFixtureFactory.CheckBox, "the ticked checkbox"),
    ];

    /// <summary>
    /// Fails unless (nearly) the whole inner area of every rectangle is dark; the failure lists every rectangle
    /// that is not, so one run shows everything that is missing.
    /// </summary>
    private void AssertAllInked(Bitmap page, (PdfRect Area, string What)[] marks)
    {
        var missing = new List<string>();
        foreach ((PdfRect area, string what) in marks)
        {
            Rectangle inner = area.ToInnerPixels(Dpi);
            int dark = Pixels.CountDarkIn(page, inner);
            int total = inner.Width * inner.Height;
            _output.WriteLine($"Dark pixels in {what}: {dark} of {total}");
            if (dark < total * FullyInked)
            {
                missing.Add($"{what}: only {dark} of its {total} pixels are dark");
            }
        }

        Assert.True(missing.Count == 0, "Expected these to be drawn: " + string.Join("; ", missing) + ".");
    }

    /// <summary>
    /// Fails unless a fixture page whose fields were flattened into the page itself (the fallback when its copy
    /// cannot be used) shows both printable fields and its own content. With its own resources, nothing PDFium
    /// draws is lost. With resources taken from the page tree, the form XObject and the image drawn through them
    /// are missing: that is the documented limitation of the fallback, asserted here so that a change in it is
    /// noticed (if it is ever solved, this branch of the check must change with the docs).
    /// </summary>
    private void AssertFieldsDrawnIntoThePageItself(Bitmap ours, Bitmap reference, bool inherited, RenderPath path)
    {
        AssertInked(ours, FormFixtureFactory.TextField, "the filled text field");
        AssertInked(ours, FormFixtureFactory.CheckBox, "the ticked checkbox");
        AssertInked(ours, FormFixtureFactory.PageMark, "the page's own black square");
        (int lost, int gained) = CompareInk(reference, ours);
        _output.WriteLine($"{path}, {(inherited ? "inherited" : "own")} resources: lost {lost}, gained {gained}");
        if (inherited)
        {
            AssertNotInked(ours, FormFixtureFactory.ResourceXObject, "the form XObject drawn through the inherited resources (documented limitation)");
            AssertNotInked(ours, FormFixtureFactory.ResourceImage, "the image drawn through the inherited resources (documented limitation)");
        }
        else
        {
            Assert.Equal(0, lost);
            AssertFieldsAddTheirAreaAndNothingElse(gained, FieldArea);
        }
    }

    /// <summary>Fails unless (nearly) none of the inner area of <paramref name="area"/> is dark.</summary>
    private void AssertNotInked(Bitmap page, PdfRect area, string what)
    {
        Rectangle inner = area.ToInnerPixels(Dpi);
        int dark = Pixels.CountDarkIn(page, inner);
        int total = inner.Width * inner.Height;
        _output.WriteLine($"Dark pixels in {what}: {dark} of {total}");
        Assert.True(dark <= total * 0.05, $"Expected {what} to be missing: {dark} of its {total} pixels are dark.");
    }

    /// <summary>Fails unless (nearly) the whole inner area of <paramref name="field"/> is dark.</summary>
    private void AssertInked(Bitmap page, PdfRect field, string what) => AssertInked(page, field.ToInnerPixels(Dpi), what);

    /// <summary>Fails unless (nearly) the whole of <paramref name="area"/>, in pixels, is dark.</summary>
    private void AssertInked(Bitmap page, Rectangle area, string what)
    {
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
