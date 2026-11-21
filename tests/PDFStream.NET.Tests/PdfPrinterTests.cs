// Every Print call here takes an optional CancellationToken that cancels the print job, not the test run, so
// the analyzer's advice to pass the test's own token does not apply.
#pragma warning disable xUnit1051

using System.Drawing;
using PDFStream.NET.Engine;
using PDFStream.NET.Printing;
using PDFStream.NET.Tests.Fixtures;
using PDFStream.NET.Tests.Printing;
using Xunit;

namespace PDFStream.NET.Tests;

/// <summary>
/// Tests <see cref="PdfPrinter"/>, the public facade: option checks, the order of the steps (options,
/// load, page range, printer settings, job), that bad input never throws, that every document is
/// disposed and that unexpected failures become results. The print job itself is faked through the
/// runner, except in the one test that prints onto bitmaps and checks the ink. A test that has to get
/// past the printer check uses the installed "Microsoft Print to PDF" printer (it is never printed to)
/// and skips, with the reason shown, when that printer is missing.
/// </summary>
public sealed class PdfPrinterTests
{
    /// <summary>An engine that hands out a prepared document, or throws, and counts how often it was asked.</summary>
    private sealed class FakeEngine(Func<IPdfDocument>? open = null, Exception? throws = null) : IPdfEngine
    {
        public int Loads { get; private set; }

        public PdfLoadStatus TryLoad(byte[] pdf, string? password, out IPdfDocument? document)
        {
            Loads++;
            if (throws is not null)
            {
                throw throws;
            }

            document = open!();
            return PdfLoadStatus.Ok;
        }
    }

    /// <summary>A document that forwards to another one and remembers whether it was disposed.</summary>
    private sealed class TrackedDocument(IPdfDocument inner) : IPdfDocument
    {
        public bool Disposed { get; private set; }

        public int PageCount => inner.PageCount;

        public System.Drawing.SizeF GetPageSizePoints(int pageIndex) => inner.GetPageSizePoints(pageIndex);

        public void RenderToDeviceContext(int pageIndex, IntPtr hdc, Rectangle destinationPixels, bool forPrinting, int quarterTurns) =>
            inner.RenderToDeviceContext(pageIndex, hdc, destinationPixels, forPrinting, quarterTurns);

        public Bitmap RenderToBitmap(int pageIndex, int dpi, int quarterTurns) => inner.RenderToBitmap(pageIndex, dpi, quarterTurns);

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }
    }

    /// <summary>A printer list with fixed names.</summary>
    private sealed class FakeCatalog(params string[] names) : IPrinterCatalog
    {
        public IReadOnlyList<string> GetInstalledPrinterNames() => names;
    }

    /// <summary>A job runner that records what the facade handed it instead of printing.</summary>
    private sealed class RecordingRunner
    {
        private readonly object _gate = new();

        public int Calls { get; private set; }

        public List<int[]> PageLists { get; } = [];

        public string? DocumentName { get; private set; }

        public ScaleMode? Scale { get; private set; }

        public short? Copies { get; private set; }

        public string? PrinterName { get; private set; }

        public CancellationToken Token { get; private set; }

        /// <summary>What the runner returns; when <see langword="null"/>, success with every page counted.</summary>
        public PrintResult? Result { get; init; }

        /// <summary>When set, the runner throws this instead.</summary>
        public Exception? Throws { get; init; }

        public PrintResult Run(IPdfDocument doc, IReadOnlyList<int> pages, System.Drawing.Printing.PrinterSettings settings, string documentName, ScaleMode scale, CancellationToken token)
        {
            lock (_gate)
            {
                Calls++;
                PageLists.Add(pages.ToArray());
                DocumentName = documentName;
                Scale = scale;
                Copies = settings.Copies;
                PrinterName = settings.PrinterName;
                Token = token;
            }

            if (Throws is not null)
            {
                throw Throws;
            }

            return Result ?? PrintResult.Success(pages.Count);
        }
    }

    private static PrintOptions ToPdfPrinter(string? pages = null) =>
        new() { PrinterName = TestPrinters.MicrosoftPrintToPdf, Pages = pages };

    private static (FakeEngine Engine, TrackedDocument Document) FakeLetterEngine(int pages)
    {
        var document = new TrackedDocument(FakePdfDocument.Letter(pages));
        return (new FakeEngine(() => document), document);
    }

    // The bytes only have to be non-empty: the fake engine does not read them.
    private static readonly byte[] AnyBytes = [1, 2, 3];

    /// <summary>A stream that cannot be read, like a connection that dropped.</summary>
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The connection was closed (test).");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Null options are a bad input like any other: InvalidOptions on every overload, never an exception.</summary>
    [Fact]
    public void Null_options_is_InvalidOptions()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        using var stream = new MemoryStream(pdf);

        PrintResult[] results =
        [
            PdfPrinter.Print("any.pdf", null!),
            PdfPrinter.Print(stream, null!),
            PdfPrinter.Print(pdf, null!),
        ];

        Assert.All(results, result =>
        {
            Assert.Equal(PrintStatus.InvalidOptions, result.Status);
            Assert.False(result.Succeeded);
            Assert.Equal(0, result.PagesPrinted);
            Assert.Equal(Messages.InvalidOptions(), result.Message);
        });
    }

    /// <summary>
    /// A table of bad inputs, every one of which must come back as a result with the right status, the
    /// standard message, no pages and no exception, whichever overload it goes through.
    /// </summary>
    [Fact]
    public void Bad_input_never_throws()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        PrintOptions good = ToPdfPrinter();
        string missingFile = Path.Combine(Path.GetTempPath(), "pdfstream.net-missing-" + Guid.NewGuid().ToString("N") + ".pdf");

        (string Case, Func<PrintResult> Call, PrintStatus Expected)[] table =
        [
            ("null path", () => PdfPrinter.Print((string)null!, good), PrintStatus.FileNotFound),
            ("empty path", () => PdfPrinter.Print("", good), PrintStatus.FileNotFound),
            ("blank path", () => PdfPrinter.Print("   ", good), PrintStatus.FileNotFound),
            ("missing file", () => PdfPrinter.Print(missingFile, good), PrintStatus.FileNotFound),
            ("folder instead of file", () => PdfPrinter.Print(Path.GetTempPath(), good), PrintStatus.FileNotFound),
            ("null bytes", () => PdfPrinter.Print((byte[])null!, good), PrintStatus.InvalidPdf),
            ("empty bytes", () => PdfPrinter.Print(Array.Empty<byte>(), good), PrintStatus.InvalidPdf),
            ("garbage bytes", () => PdfPrinter.Print(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0x10 }, good), PrintStatus.InvalidPdf),
            ("text that is not a PDF", () => PdfPrinter.Print("This is not a PDF."u8.ToArray(), good), PrintStatus.InvalidPdf),
            ("null stream", () => PdfPrinter.Print((Stream)null!, good), PrintStatus.InvalidPdf),
            ("empty stream", () => PdfPrinter.Print(new MemoryStream(), good), PrintStatus.InvalidPdf),
            ("broken stream", () => PdfPrinter.Print(new BrokenStream(), good), PrintStatus.InvalidPdf),
            ("empty printer name", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "" }), PrintStatus.InvalidOptions),
            ("default options (no printer name)", () => PdfPrinter.Print(pdf, new PrintOptions()), PrintStatus.InvalidOptions),
            ("blank printer name", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "   " }), PrintStatus.InvalidOptions),
            ("Copies 0", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Copies = 0 }), PrintStatus.InvalidOptions),
            ("Copies -1", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Copies = -1 }), PrintStatus.InvalidOptions),
            ("Copies too many", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Copies = 40000 }), PrintStatus.InvalidOptions),
            ("undefined Duplex", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Duplex = (DuplexMode)99 }), PrintStatus.InvalidOptions),
            ("undefined Scale", () => PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Scale = (ScaleMode)99 }), PrintStatus.InvalidOptions),
            ("options are checked before the file", () => PdfPrinter.Print(missingFile, new PrintOptions()), PrintStatus.InvalidOptions),
            ("malformed page range", () => PdfPrinter.Print(pdf, ToPdfPrinter("5-2")), PrintStatus.InvalidPageRange),
            ("page range beyond the document", () => PdfPrinter.Print(pdf, ToPdfPrinter("2")), PrintStatus.PageNotFound),
        ];

        foreach ((string name, Func<PrintResult> call, PrintStatus expected) in table)
        {
            PrintResult result = call();

            Assert.True(result.Status == expected, $"{name}: expected {expected} but got {result.Status} ({result.Message}).");
            Assert.False(result.Succeeded, name);
            Assert.Equal(0, result.PagesPrinted);
            Assert.Equal(Messages.For(expected), result.Message);
        }
    }

    /// <summary>A protected PDF reports whether a password is missing or wrong, before any printer work.</summary>
    [Fact]
    public void Protected_pdf_reports_missing_and_wrong_password()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1, userPassword: "open-sesame");
        var runner = new RecordingRunner();

        PrintResult missing = PdfPrinter.Print(pdf, ToPdfPrinter(), new PdfiumEngine(), new FakeCatalog(), runner.Run);
        PrintResult wrong = PdfPrinter.Print(pdf, new PrintOptions { PrinterName = "P", Password = "nope" }, new PdfiumEngine(), new FakeCatalog(), runner.Run);

        Assert.Equal(PrintStatus.PasswordRequired, missing.Status);
        Assert.Equal(PrintStatus.PasswordInvalid, wrong.Status);
        Assert.Equal(0, runner.Calls);
    }

    /// <summary>An unknown printer is PrinterNotFound; the job is never started and the document is released.</summary>
    [Fact]
    public void Unknown_printer_is_PrinterNotFound_and_nothing_printed()
    {
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(2);
        var runner = new RecordingRunner();

        PrintResult result = PdfPrinter.Print(AnyBytes, new PrintOptions { PrinterName = "No such printer" }, engine, new FakeCatalog("Another printer"), runner.Run);

        Assert.Equal(PrintStatus.PrinterNotFound, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(0, runner.Calls);
        Assert.True(document.Disposed);
    }

    /// <summary>
    /// A PDF that is protected only by an owner password (restrictions, no open password) prints with no
    /// password given. The job runs through the real print code onto bitmaps, so the test checks that
    /// ink reached every sheet, not only the status.
    /// </summary>
    [Fact]
    public void Owner_only_password_pdf_prints_without_password()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        byte[] pdf = FixtureFactory.CreatePdf(2, ownerPassword: "owner-secret");
        using var controller = new FakePrintController();

        PrintResult result = PdfPrinter.Print(
            pdf,
            ToPdfPrinter(),
            new PdfiumEngine(),
            SystemPrinterCatalog.Instance,
            (doc, pages, settings, name, scale, token) => PrintJob.Run(doc, pages, settings, name, scale, token, controller));

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.PagesPrinted);
        Assert.Equal(2, controller.Pages.Count);
        Assert.All(controller.Pages, sheet => Assert.True(Pixels.CountDark(sheet) > 50, "Expected the page's text on every sheet."));
    }

    /// <summary>Only the pages named in the range reach the job, in the order written, with the repeats written.</summary>
    [Fact]
    public void Page_range_selects_only_those_pages()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(5);
        var runner = new RecordingRunner();

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter("4, 2-3, 4"), engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.Equal(4, result.PagesPrinted);
        Assert.Equal(1, runner.Calls);
        Assert.Equal([3, 1, 2, 3], runner.PageLists.Single());
        Assert.True(document.Disposed);
    }

    /// <summary>No page range means every page, and the options reach the job as given.</summary>
    [Fact]
    public void No_page_range_prints_every_page_and_passes_the_options_on()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        (FakeEngine engine, _) = FakeLetterEngine(3);
        var runner = new RecordingRunner();
        using var cancellation = new CancellationTokenSource();
        var options = new PrintOptions
        {
            PrinterName = TestPrinters.MicrosoftPrintToPdf,
            DocumentName = "Delivery note 7",
            Copies = 3,
            Scale = ScaleMode.ActualSize,
        };

        PrintResult result = PdfPrinter.Print(AnyBytes, options, engine, SystemPrinterCatalog.Instance, runner.Run, cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Equal([0, 1, 2], runner.PageLists.Single());
        Assert.Equal("Delivery note 7", runner.DocumentName);
        Assert.Equal(ScaleMode.ActualSize, runner.Scale);
        Assert.Equal((short)3, runner.Copies);
        Assert.Equal(TestPrinters.MicrosoftPrintToPdf, runner.PrinterName, ignoreCase: true);
        Assert.Equal(cancellation.Token, runner.Token);
    }

    /// <summary>
    /// Whatever the job returns is passed through untouched: a failure keeps its status, message and its
    /// zero pages, and a cancellation that ended the job normally keeps the exact page count.
    /// </summary>
    [Fact]
    public void Job_result_is_passed_through_untouched()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        PrintResult[] fromJob =
        [
            PrintResult.Failure(PrintStatus.UnknownError, "Page 2 failed (test)."),
            PrintResult.Failure(PrintStatus.Cancelled, pagesPrinted: 2),
        ];

        foreach (PrintResult expected in fromJob)
        {
            (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(4);
            var runner = new RecordingRunner { Result = expected };

            PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter(), engine, SystemPrinterCatalog.Instance, runner.Run);

            Assert.Same(expected, result);
            Assert.True(document.Disposed);
        }
    }

    /// <summary>A page beyond the document is PageNotFound; nothing is printed and the document is released.</summary>
    [Fact]
    public void Page_range_beyond_count_is_PageNotFound_and_nothing_printed()
    {
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(3);
        var runner = new RecordingRunner();

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter("2-4"), engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.PageNotFound, result.Status);
        Assert.Equal(Messages.PageNotFound(), result.Message);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(0, runner.Calls);
        Assert.True(document.Disposed);
    }

    /// <summary>A malformed page range is InvalidPageRange, found after the document opened and before any printer work.</summary>
    [Fact]
    public void Malformed_page_range_is_InvalidPageRange_and_nothing_printed()
    {
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(3);
        var runner = new RecordingRunner();

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter("99999999999-2"), engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.InvalidPageRange, result.Status);
        Assert.Equal(0, runner.Calls);
        Assert.True(document.Disposed);
    }

    /// <summary>
    /// Several threads printing at once all succeed: the engine serialises its own work, so the calls wait
    /// for each other instead of failing. The runner renders a page of the real document so the engine is
    /// really used by every thread, and the ink is checked.
    /// </summary>
    [Fact]
    public void Two_concurrent_prints_both_succeed()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        var engine = new PdfiumEngine();
        const int threads = 8;
        var results = new PrintResult[threads];
        var ink = new int[threads];

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            byte[] pdf = FixtureFactory.CreatePdf(1 + (i % 3));
            results[i] = PdfPrinter.Print(
                pdf,
                ToPdfPrinter(),
                engine,
                SystemPrinterCatalog.Instance,
                (doc, pages, settings, name, scale, token) =>
                {
                    using Bitmap page = doc.RenderToBitmap(pages[0], 40, 0);
                    ink[i] = Pixels.CountDark(page);
                    return PrintResult.Success(pages.Count);
                });
        });

        for (int i = 0; i < threads; i++)
        {
            Assert.True(results[i].Succeeded, $"Thread {i}: {results[i].Status} {results[i].Message}");
            Assert.Equal(1 + (i % 3), results[i].PagesPrinted);
            Assert.True(ink[i] > 0, $"Thread {i} rendered a blank page.");
        }
    }

    /// <summary>
    /// A failure of the engine itself (here a missing native library) is not thrown: it becomes
    /// UnknownError, with the exception's message after the standard text for the person who looks after
    /// the program.
    /// </summary>
    [Fact]
    public void Unexpected_engine_exception_becomes_UnknownError()
    {
        var engine = new FakeEngine(throws: new DllNotFoundException("pdfium.dll could not be loaded (test)."));
        var runner = new RecordingRunner();

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter(), engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.StartsWith(Messages.UnknownError(), result.Message);
        Assert.Contains("pdfium.dll could not be loaded (test).", result.Message);
        Assert.Equal(0, runner.Calls);
    }

    /// <summary>An exception thrown while the job runs is also a result, and the document is still released.</summary>
    [Fact]
    public void Unexpected_job_exception_becomes_UnknownError_and_the_document_is_released()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(2);
        var runner = new RecordingRunner { Throws = new InvalidOperationException("The job exploded (test).") };

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter(), engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Contains("The job exploded (test).", result.Message);
        Assert.True(document.Disposed);
    }

    /// <summary>An exception with an empty message still says something in the details.</summary>
    [Fact]
    public void Exception_without_a_message_still_names_its_type()
    {
        var engine = new FakeEngine(throws: new DllNotFoundException(""));

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter(), engine, SystemPrinterCatalog.Instance, new RecordingRunner().Run);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.Contains("DllNotFoundException", result.Message);
    }

    /// <summary>A token that is already cancelled stops everything before the PDF is even opened.</summary>
    [Fact]
    public void Cancelled_before_start_returns_Cancelled_with_zero_pages()
    {
        var engine = new FakeEngine(() => FakePdfDocument.Letter(1));
        var runner = new RecordingRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        PrintResult result = PdfPrinter.Print(AnyBytes, ToPdfPrinter(), engine, SystemPrinterCatalog.Instance, runner.Run, cancellation.Token);

        Assert.Equal(PrintStatus.Cancelled, result.Status);
        Assert.Equal(Messages.Cancelled(), result.Message);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(0, engine.Loads);
        Assert.Equal(0, runner.Calls);
    }

    /// <summary>The public overloads honour the token too.</summary>
    [Fact]
    public void Public_overloads_return_Cancelled_for_a_cancelled_token()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream(pdf);

        PrintResult[] results =
        [
            PdfPrinter.Print("any.pdf", ToPdfPrinter(), cancellation.Token),
            PdfPrinter.Print(stream, ToPdfPrinter(), cancellation.Token),
            PdfPrinter.Print(pdf, ToPdfPrinter(), cancellation.Token),
        ];

        Assert.All(results, result =>
        {
            Assert.Equal(PrintStatus.Cancelled, result.Status);
            Assert.Equal(0, result.PagesPrinted);
        });
    }

    /// <summary>
    /// The printer lookup's own message (here the list of paper sources) reaches the result, not just the
    /// standard text of the status.
    /// </summary>
    [Fact]
    public void Paper_source_message_reaches_the_result()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        (FakeEngine engine, TrackedDocument document) = FakeLetterEngine(1);
        var runner = new RecordingRunner();
        var options = new PrintOptions { PrinterName = TestPrinters.MicrosoftPrintToPdf, PaperSource = "Tray that does not exist 42" };

        PrintResult result = PdfPrinter.Print(AnyBytes, options, engine, SystemPrinterCatalog.Instance, runner.Run);

        Assert.Equal(PrintStatus.InvalidOptions, result.Status);
        Assert.NotEqual(Messages.InvalidOptions(), result.Message);
        Assert.Contains("Tray that does not exist 42", result.Message);
        Assert.StartsWith("The paper source", result.Message);
        Assert.Equal(0, runner.Calls);
        Assert.True(document.Disposed);
    }

    /// <summary>The path overload reads a real file and the stream overload reads from the stream's current position.</summary>
    [Fact]
    public void Path_and_stream_overloads_load_the_pdf()
    {
        TestPrinters.RequireMicrosoftPrintToPdf();
        byte[] pdf = FixtureFactory.CreatePdf(2);
        string path = Path.Combine(Path.GetTempPath(), "pdfstream.net-facade-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var runner = new RecordingRunner();
            using var stream = new MemoryStream(pdf);

            PrintResult fromPath = PdfPrinter.Print(path, ToPdfPrinter(), new PdfiumEngine(), SystemPrinterCatalog.Instance, runner.Run);
            PrintResult fromStream = PdfPrinter.Print(stream, ToPdfPrinter("2"), new PdfiumEngine(), SystemPrinterCatalog.Instance, runner.Run);

            Assert.True(fromPath.Succeeded);
            Assert.True(fromStream.Succeeded);
            Assert.Equal([[0, 1], [1]], runner.PageLists);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
