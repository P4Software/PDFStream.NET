using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PDFStream.net.Engine;
using PDFStream.net.Printing;
using PDFStream.net.Tests.Fixtures;
using Xunit;

namespace PDFStream.net.Tests.Acceptance;

/// <summary>
/// Acceptance tests that print for real: through the Windows print system to the "Microsoft Print to
/// PDF" printer, written to a file with <see cref="PrintOptions.OutputFile"/> so no Save As box ever
/// opens. Each test then re-opens the produced PDF with the library's own engine and checks the page
/// count and that every page holds ink, because "printed" must mean pages were drawn, not only that a
/// status said so.
/// </summary>
/// <remarks>
/// <para>
/// Run only these with the xUnit trait filter: <c>dotnet test --project tests/PDFStream.net.Tests --filter-trait "Category=RealPrinter"</c>.
/// A machine without the printer skips every test with a visible reason instead of failing.
/// </para>
/// <para>
/// Safety: every real print runs under a hard <see cref="PrintTimeout"/>. If a dialog ever did open, or the
/// spooler stopped answering, the test fails with a message instead of hanging the run. Output goes only
/// into a folder of its own per test, which is deleted afterwards.
/// </para>
/// </remarks>
[Trait("Category", "RealPrinter")]
public sealed class RealPrintTests : IDisposable
{
    /// <summary>The printer every test prints to. It ships with Windows 10 and 11 and writes a PDF file.</summary>
    private const string PrinterName = "Microsoft Print to PDF";

    /// <summary>The longest a single print may take before the test fails instead of waiting on a hidden dialog.</summary>
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to wait, after the print call returned, for the output file to be complete and unlocked. Usually it
    /// takes a second or two, but on a busy machine the driver once needed more than 30 seconds (a 32-bit run that
    /// took over two minutes in all), so the wait is generous; a printer that never finishes still fails the test,
    /// with the state of its queue in the message.
    /// </summary>
    private static readonly TimeSpan OutputFileTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The resolution the produced pages are rendered at when counting ink. 40 dpi keeps a page small and
    /// fast to render while 36 point text still covers hundreds of pixels.
    /// </summary>
    private const int CheckDpi = 40;

    /// <summary>
    /// The fewest non-white pixels a page must hold to count as printed. A blank page has none; the fixtures'
    /// large text produces several hundred even at 40 dpi, so this is far from both extremes.
    /// </summary>
    private const int MinInkPixels = 50;

    private const string UserPassword = "open-sesame";

    private readonly string _folder;
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class: a new empty temporary folder for this test's output.</summary>
    public RealPrintTests(ITestOutputHelper output)
    {
        _output = output;
        _folder = Path.Combine(Path.GetTempPath(), "PDFStream.net-realprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _output.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}, runtime: {RuntimeInformation.FrameworkDescription}");
    }

    /// <summary>Deletes the temporary folder. A file still held by a stuck spooler job is left to the system's temp cleanup.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: failing to delete test output must not turn a passing test red.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: the folder is under the temp path and is harmless if it stays.
        }
    }

    /// <summary>A three-page PDF prints to a file that has three pages, each with ink on it.</summary>
    [Fact]
    public void Prints_three_page_pdf_to_file_with_three_pages_and_non_blank_output()
    {
        RequirePrinter();
        string output = OutputPath("three.pdf");

        PrintResult result = PrintWithTimeout(FixtureFactory.CreatePdf(3), new PrintOptions { PrinterName = PrinterName, OutputFile = output });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.PagesPrinted);
        AssertOutputPages(output, expectedPages: 3);
    }

    /// <summary>Five pages of mixed sizes and orientations all come out, none blank.</summary>
    [Fact]
    public void Mixed_sizes_and_orientations_print_every_page()
    {
        RequirePrinter();
        string output = OutputPath("mixed.pdf");

        PrintResult result = PrintWithTimeout(FixtureFactory.CreatePdf(5, mixedSizes: true), new PrintOptions { PrinterName = PrinterName, OutputFile = output });

        Assert.True(result.Succeeded, result.Message);
        AssertOutputPages(output, expectedPages: 5);
    }

    /// <summary>A page range prints only the pages named, not the whole document.</summary>
    [Fact]
    public void Page_range_prints_only_selected_pages()
    {
        RequirePrinter();
        string output = OutputPath("range.pdf");

        PrintResult result = PrintWithTimeout(FixtureFactory.CreatePdf(5), new PrintOptions { PrinterName = PrinterName, OutputFile = output, Pages = "2, 4-5" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.PagesPrinted);
        AssertOutputPages(output, expectedPages: 3);
    }

    /// <summary>
    /// Two copies give a file with every page twice. Microsoft Print to PDF cannot make copies itself, so
    /// this is the library's software copy path; a driver that does make copies is skipped, because its
    /// file would hold the pages once.
    /// </summary>
    [Fact]
    public void Copies_two_produces_doubled_pages()
    {
        RequirePrinter();
        int driverMaximum = new System.Drawing.Printing.PrinterSettings { PrinterName = PrinterName }.MaximumCopies;
        Assert.SkipWhen(driverMaximum >= 2, $"This machine's '{PrinterName}' driver makes copies itself (it reports {driverMaximum}), so the output file would not hold the pages twice.");
        string output = OutputPath("copies.pdf");

        PrintResult result = PrintWithTimeout(FixtureFactory.CreatePdf(3), new PrintOptions { PrinterName = PrinterName, OutputFile = output, Copies = 2 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(6, result.PagesPrinted);
        AssertOutputPages(output, expectedPages: 6);
    }

    /// <summary>A PDF that needs a password prints when the right one is given.</summary>
    [Fact]
    public void Password_protected_pdf_prints_with_right_password()
    {
        RequirePrinter();
        string output = OutputPath("protected.pdf");

        PrintResult result = PrintWithTimeout(
            FixtureFactory.CreatePdf(2, userPassword: UserPassword),
            new PrintOptions { PrinterName = PrinterName, OutputFile = output, Password = UserPassword });

        Assert.True(result.Succeeded, result.Message);
        AssertOutputPages(output, expectedPages: 2);
    }

    /// <summary>A wrong password is refused with <see cref="PrintStatus.PasswordInvalid"/> and no file is written.</summary>
    [Fact]
    public void Wrong_password_returns_PasswordInvalid_and_writes_nothing()
    {
        RequirePrinter();
        string output = OutputPath("wrong.pdf");

        PrintResult result = PrintWithTimeout(
            FixtureFactory.CreatePdf(2, userPassword: UserPassword),
            new PrintOptions { PrinterName = PrinterName, OutputFile = output, Password = "not-the-password" });

        Assert.Equal(PrintStatus.PasswordInvalid, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.False(File.Exists(output), "A refused job must not leave an output file.");
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    /// <summary>
    /// Filled-in form fields reach the paper. The fixture page holds nothing but its fields (a filled text field
    /// and a ticked checkbox marked for printing, and one screen-only field), so a blank output page means the
    /// fields were lost on the way to the printer, which is what happened before PDFStream.net flattened them.
    /// </summary>
    [Fact]
    public void Filled_form_fields_print()
    {
        RequirePrinter();
        string output = OutputPath("form.pdf");

        PrintResult result = PrintWithTimeout(FormFixtureFactory.Create(), new PrintOptions { PrinterName = PrinterName, OutputFile = output });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.PagesPrinted);
        AssertOutputPages(output, expectedPages: 1);
    }

    /// <summary>
    /// Print to PDF accepts the page as drawing commands on the printer's device context, so the bitmap
    /// fallback is never used. The page painter has no public or internal switch that reports this, so the
    /// check goes through the engine seam the facade already has: a wrapper around the real engine counts
    /// which drawing calls the job makes.
    /// </summary>
    [Fact]
    public void Print_to_pdf_uses_the_vector_path_not_the_bitmap_fallback()
    {
        RequirePrinter();
        string output = OutputPath("vector.pdf");
        var spy = new SpyEngine(new PdfiumEngine());
        var options = new PrintOptions { PrinterName = PrinterName, OutputFile = output };

        PrintResult result = RunWithTimeout(() => PdfPrinter.Print(
            FixtureFactory.CreatePdf(3),
            options,
            spy,
            SystemPrinterCatalog.Instance,
            (doc, pages, settings, name, scale, token) => PrintJob.Run(doc, pages, settings, name, scale, token)));

        _output.WriteLine($"Device context renders: {spy.DeviceContextRenders}, bitmap renders: {spy.BitmapRenders}");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, spy.DeviceContextRenders);
        Assert.Equal(0, spy.BitmapRenders);
        AssertOutputPages(output, expectedPages: 3);
    }

    /// <summary>Skips the test, with the reason shown in the run, when this machine has no Print to PDF printer.</summary>
    private static void RequirePrinter()
    {
        bool installed = false;
        try
        {
            installed = PrinterLocator.FindInstalledName(SystemPrinterCatalog.Instance, PrinterName) is not null;
        }
        catch (Exception)
        {
            // An unreachable print system counts as "no printer" for the purpose of skipping.
        }

        Assert.SkipUnless(installed, $"The printer '{PrinterName}' is not installed on this machine, so the real-print tests cannot run. Turn on the Windows feature 'Microsoft Print to PDF' and run them again.");
    }

    private string OutputPath(string fileName) => Path.Combine(_folder, fileName);

    /// <summary>Prints a PDF held in memory through the public API, under the hard timeout.</summary>
    private static PrintResult PrintWithTimeout(byte[] pdf, PrintOptions options) =>
        RunWithTimeout(() => PdfPrinter.Print(pdf, options));

    /// <summary>
    /// Runs a print on a worker thread and waits at most <see cref="PrintTimeout"/>. A printer dialog
    /// waiting for a click would otherwise block the test run forever; here it fails the test with a
    /// clear reason. The abandoned worker is a thread-pool thread and does not keep the process alive.
    /// </summary>
    private static PrintResult RunWithTimeout(Func<PrintResult> print)
    {
        Task<PrintResult> job = Task.Run(print);
        try
        {
            return job.WaitAsync(PrintTimeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            Assert.Fail($"The print did not finish within {PrintTimeout.TotalSeconds:0} seconds. A hidden Save As dialog or a stuck print spooler is the likely cause: close any open 'Save Print Output As' window and check the printer queue.");
            throw; // Unreachable: Assert.Fail always throws. It satisfies the compiler's flow analysis.
        }
    }

    /// <summary>
    /// Re-opens the produced file with the library's engine and checks it has the expected number of pages
    /// and that every page holds ink. A blank page fails.
    /// </summary>
    private void AssertOutputPages(string path, int expectedPages)
    {
        byte[] bytes = WaitForOutputFile(path);

        PdfLoadStatus status = new PdfiumEngine().TryLoad(bytes, null, out IPdfDocument? document);
        using (document)
        {
            Assert.Equal(PdfLoadStatus.Ok, status);
            Assert.NotNull(document);
            Assert.Equal(expectedPages, document.PageCount);

            for (int page = 0; page < document.PageCount; page++)
            {
                using Bitmap bitmap = document.RenderToBitmap(page, CheckDpi, quarterTurns: 0);
                int ink = CountNonWhite(bitmap);
                _output.WriteLine($"Output page {page + 1}: {bitmap.Width}x{bitmap.Height} px at {CheckDpi} dpi, {ink} non-white pixels");
                Assert.True(ink >= MinInkPixels, $"Output page {page + 1} is blank or nearly blank ({ink} non-white pixels).");
            }
        }
    }

    /// <summary>
    /// Waits until the print system has finished writing the output file and returns its bytes. The print
    /// call returns when the job has been handed to the spooler; Microsoft Print to PDF then writes and
    /// closes the file on its own schedule, so a read straight after the call can find the file missing,
    /// empty or still locked (seen in the 32-bit test host). Any real caller that wants to open the file
    /// at once needs the same patience, which is why the wait is explicit here and bounded.
    /// </summary>
    private byte[] WaitForOutputFile(string path)
    {
        DateTime giveUp = DateTime.UtcNow + OutputFileTimeout;
        Exception? lastProblem = null;
        while (DateTime.UtcNow < giveUp)
        {
            try
            {
                // FileShare.None succeeds only once the writer has closed the file.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length > 0)
                {
                    var bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes);
                    return bytes;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not there yet, or still being written: try again shortly.
                lastProblem = ex;
            }

            Thread.Sleep(100);
        }

        Assert.Fail($"The printer did not finish writing {path} within {OutputFileTimeout.TotalSeconds:0} seconds. Last problem: {lastProblem?.Message ?? "the file stayed empty or missing"}. Print queue '{PrinterName}': {DescribeQueue()}.");
        throw new InvalidOperationException("Unreachable: Assert.Fail always throws.");
    }

    /// <summary>
    /// The state of the printer's queue as Windows reports it (its status and attribute bits and the number of jobs
    /// in it), for a failure message: a paused, stuck or busy queue explains an output file that never appears.
    /// Never throws.
    /// </summary>
    private static string DescribeQueue()
    {
        try
        {
            PrinterStatusReadOutcome outcome = WinspoolPrinterStatusProbe.Instance.TryRead(PrinterName, out uint status, out uint attributes);
            string jobs = CountJobs(PrinterName) is int count ? $"{count} job(s) in the queue" : "the number of jobs could not be read";
            return outcome == PrinterStatusReadOutcome.Read
                ? $"status 0x{status:X8}, attributes 0x{attributes:X8}, {jobs}"
                : $"status not readable ({outcome}), {jobs}";
        }
        catch (Exception ex)
        {
            return $"not readable ({ex.Message})";
        }
    }

    /// <summary>The number of jobs in a printer's queue (PRINTER_INFO_2 cJobs), or <see langword="null"/> when it cannot be read.</summary>
    private static int? CountJobs(string printerName)
    {
        if (!OpenPrinter(printerName, out IntPtr printer, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            // The first call only asks for the size of the variable-length block, and fails by design.
            GetPrinter(printer, 2, IntPtr.Zero, 0, out int needed);
            if (needed < Marshal.SizeOf<WinspoolPrinterStatusProbe.PrinterInfo2>())
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                return GetPrinter(printer, 2, buffer, needed, out _)
                    ? (int)Marshal.PtrToStructure<WinspoolPrinterStatusProbe.PrinterInfo2>(buffer).Jobs
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    /// <summary>OpenPrinterW, from the documented Win32 API.</summary>
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    /// <summary>GetPrinterW, from the documented Win32 API.</summary>
    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, int level, IntPtr buffer, int bufferSize, out int needed);

    /// <summary>ClosePrinter, from the documented Win32 API.</summary>
    [DllImport("winspool.drv", ExactSpelling = true, SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    /// <summary>Counts pixels that are not (nearly) white. Near-white is allowed for because anti-aliasing and compression soften the paper colour.</summary>
    private static int CountNonWhite(Bitmap bitmap)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int count = 0;
            var row = new int[bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                foreach (int argb in row)
                {
                    if (((argb >> 16) & 0xFF) < 245 || ((argb >> 8) & 0xFF) < 245 || (argb & 0xFF) < 245)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>An engine that opens documents with the real engine and counts the drawing calls made on them.</summary>
    private sealed class SpyEngine(IPdfEngine inner) : IPdfEngine
    {
        private int _deviceContextRenders;
        private int _bitmapRenders;

        /// <summary>Pages sent to the printer's device context (the vector path).</summary>
        public int DeviceContextRenders => Volatile.Read(ref _deviceContextRenders);

        /// <summary>Pages rendered to a bitmap (the fallback, which must not happen for Print to PDF).</summary>
        public int BitmapRenders => Volatile.Read(ref _bitmapRenders);

        /// <inheritdoc />
        public PdfLoadStatus TryLoad(byte[] pdf, string? password, out IPdfDocument? document)
        {
            PdfLoadStatus status = inner.TryLoad(pdf, password, out IPdfDocument? real);
            document = real is null ? null : new SpyDocument(real, this);
            return status;
        }

        private sealed class SpyDocument(IPdfDocument real, SpyEngine owner) : IPdfDocument
        {
            public int PageCount => real.PageCount;

            public SizeF GetPageSizePoints(int pageIndex) => real.GetPageSizePoints(pageIndex);

            public void RenderToDeviceContext(int pageIndex, IntPtr hdc, Rectangle destinationPixels, bool forPrinting, int quarterTurns)
            {
                // Counted only when the engine accepted the drawing: a refusal throws and the painter falls back.
                real.RenderToDeviceContext(pageIndex, hdc, destinationPixels, forPrinting, quarterTurns);
                Interlocked.Increment(ref owner._deviceContextRenders);
            }

            public Bitmap RenderToBitmap(int pageIndex, int dpi, int quarterTurns)
            {
                Interlocked.Increment(ref owner._bitmapRenders);
                return real.RenderToBitmap(pageIndex, dpi, quarterTurns);
            }

            public void Dispose() => real.Dispose();
        }
    }
}
