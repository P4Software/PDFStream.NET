using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using PDFStream.NET.Engine;
using PDFStream.NET.Printing;
using PDFStream.NET.Tests.Fixtures;
using PDFStream.NET.Tests.Printing;
using Xunit;

namespace PDFStream.NET.Tests.Engine;

/// <summary>
/// The collection of tests that measure the memory of the whole test process. It runs on its own, after the
/// tests that may run in parallel, so other tests' allocations do not blur the numbers.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MemoryMeasurementCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Memory measurements, run alone";
}

/// <summary>
/// Printing many form pages that share one big resource dictionary must cost about one copy of what they share,
/// not one copy per form page. Copying form pages one by one (each with its own PDFium import) cloned every
/// image of a shared dictionary again for every page: drawing 40 such pages sharing 8 MB of images took about
/// 290 MB more than without the copies. Printing 500 of them in a 32-bit process did not finish: the test
/// process ended with exit code 0x4000001F and no test result (most likely PDFium running out of address
/// space), and in another run the print failed part way with a misleading "page could not be drawn" message.
/// </summary>
/// <remarks>
/// The numbers are the private memory of the test process (managed and native), read after a full garbage
/// collection. They are logged for every run, so the cost can be compared over time; the assertions only use a
/// generous ceiling that the multiplied cost exceeds several times over.
/// </remarks>
[Collection(MemoryMeasurementCollection.Name)]
public sealed class FormMemoryTests
{
    /// <summary>How many one-megabyte images the pages share.</summary>
    private const int SharedImages = 8;

    /// <summary>72 dpi makes one pixel one point.</summary>
    private const int Dpi = 72;

    /// <summary>
    /// The most the private memory may grow, in megabytes, while 40 pages sharing 8 MB of images are drawn. Drawing
    /// them without any copy grew it by about 6 to 8 MB; one copy of the shared images adds about 8 MB more.
    /// Copying page by page grew it by about 300 MB, far over this ceiling.
    /// </summary>
    private const long GrowthCeilingMegabytes = 64;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with xUnit's per-test output sink.</summary>
    public FormMemoryTests(ITestOutputHelper output)
    {
        _output = output;
        _output.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}, runtime: {RuntimeInformation.FrameworkDescription}");
    }

    /// <summary>
    /// Drawing 40 pages that share 8 MB of images, inherited from the page tree or named by every page as one
    /// indirect dictionary, with a form field on every page (and, for comparison, without any field): every page
    /// shows its field (if it has one) and its image, and the private memory grows by no more than the ceiling.
    /// </summary>
    [Theory]
    [InlineData(SharedResources.Inherited, true)]
    [InlineData(SharedResources.Indirect, true)]
    [InlineData(SharedResources.Inherited, false)]
    public void Forty_pages_that_share_8_MB_of_images_cost_about_one_copy_of_them(SharedResources resources, bool withFields)
    {
        const int Pages = 40;
        byte[] pdf = SharedResourceFixtureFactory.Create(Pages, SharedImages, withFields, resources);
        var engine = new PdfiumEngine();

        long before = PrivateMegabytes();
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));
        long loaded;
        long drawn;
        var missing = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        using (document)
        {
            loaded = PrivateMegabytes();
            for (int page = 0; page < Pages; page++)
            {
                using Bitmap picture = document!.RenderToBitmap(page, Dpi, quarterTurns: 0);
                if (!FullyDark(picture, SharedResourceFixtureFactory.ImageMark))
                {
                    missing.Add($"page {page + 1}: image");
                }

                if (withFields && !FullyDark(picture, SharedResourceFixtureFactory.FieldOn(page)))
                {
                    missing.Add($"page {page + 1}: field");
                }
            }

            stopwatch.Stop();
            drawn = PrivateMegabytes();
        }

        long closed = PrivateMegabytes();
        _output.WriteLine($"{Pages} pages, {resources} resources, fields {withFields}, PDF {pdf.Length / (1024 * 1024)} MB: private memory {before} MB before loading, {loaded} MB loaded, {drawn} MB after drawing every page (+{drawn - loaded} MB), {closed} MB after closing; drawing took {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(missing.Count == 0, "Not drawn: " + string.Join(", ", missing));
        Assert.True(drawn - loaded <= GrowthCeilingMegabytes, $"Drawing the pages grew the private memory by {drawn - loaded} MB, more than the {GrowthCeilingMegabytes} MB ceiling.");
    }

    /// <summary>
    /// A print job of 500 form pages that share 8 MB of images inherited from the page tree prints every page.
    /// It matters most in a 32-bit process (run the tests with --arch x86), where copying page by page made the
    /// print fail part way or end the process (see the class summary); it runs, and must pass, in a 64-bit
    /// process too.
    /// </summary>
    [Fact]
    public void Five_hundred_form_pages_that_share_8_MB_of_images_print_in_one_job()
    {
        const int Pages = 500;
        byte[] pdf = SharedResourceFixtureFactory.Create(Pages, SharedImages, withFields: true, SharedResources.Inherited);
        var engine = new PdfiumEngine();

        long before = PrivateMegabytes();
        Assert.Equal(PdfLoadStatus.Ok, engine.TryLoad(pdf, null, out IPdfDocument? document));
        PrintResult result;
        int sheets;
        long printed;
        var prepared = new List<(int Page, PagePreparation How, CopyRejection Why)>();
        var stopwatch = Stopwatch.StartNew();
        using (document)
        {
            ((PdfiumDocument)document!).PagePrepared = (page, how, why) => prepared.Add((page, how, why));
            // Small sheets at a low resolution: what is measured is the preparation of the pages, not the sheets.
            using var controller = new FakePrintController(sheetWidth: 100, sheetHeight: 100, dpi: 10);
            result = PrintJob.Run(document!, Enumerable.Range(0, Pages).ToArray(), TestPrinters.Settings(), "Shared resources", ScaleMode.FitToPage, CancellationToken.None, controller);
            sheets = controller.Pages.Count;
            stopwatch.Stop();
            printed = PrivateMegabytes();
        }

        _output.WriteLine($"{Pages} form pages, PDF {pdf.Length / (1024 * 1024)} MB: {result.Status}, {result.PagesPrinted} pages, {sheets} sheets in {stopwatch.ElapsedMilliseconds} ms; private memory {before} MB before, {printed} MB after printing (+{printed - before} MB). {result.Message}");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Pages, result.PagesPrinted);
        Assert.Equal(Pages, sheets);

        // Ok alone would also be the result of flattening every page into itself, which loses the inherited images:
        // every page must have printed from its copy.
        Assert.Equal(Enumerable.Range(0, Pages).Select(page => (page, PagePreparation.Copy, CopyRejection.None)), prepared);
    }

    /// <summary>Whether (nearly) the whole inner area of a rectangle of a fixture page is dark.</summary>
    private static bool FullyDark(Bitmap picture, PdfRect area)
    {
        Rectangle inner = area.ToInnerPixels(Dpi, SharedResourceFixtureFactory.PageHeightPoints);
        return Pixels.CountDarkIn(picture, inner) >= inner.Width * inner.Height * 0.95;
    }

    /// <summary>The private memory of this process in megabytes, after a full garbage collection.</summary>
    private static long PrivateMegabytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64 / (1024 * 1024);
    }
}
