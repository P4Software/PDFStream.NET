using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using PDFStream.NET.Engine;
using PDFStream.NET.Printing;
using PDFStream.NET.Tests.Fixtures;
using Xunit;

namespace PDFStream.NET.Tests.Printing;

/// <summary>
/// Tests <see cref="PrintJob"/>: the page loop, cancellation, software copies, the bitmap fallback and
/// how printer and page failures become statuses. No test uses a printer: the jobs run through
/// <see cref="FakePrintController"/>, which prints onto bitmaps that the tests inspect.
/// </summary>
public sealed class PrintJobTests
{
    private static readonly TimeSpan NoHang = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Pins the test harness itself: a print document with a printer that does not exist reaches its
    /// page events through the fake controller, so every other test here really runs the print loop.
    /// </summary>
    [Fact]
    public void Test_harness_prints_two_pages_without_any_printer()
    {
        using var controller = new FakePrintController();
        using var document = new PrintDocument { PrinterSettings = TestPrinters.Settings(), PrintController = controller };
        int pages = 0;
        document.PrintPage += (_, e) =>
        {
            pages++;
            e.Graphics!.FillRectangle(Brushes.Black, 0, 0, 10, 10);
            e.HasMorePages = pages < 2;
        };

        document.Print();

        Assert.Equal(2, pages);
        Assert.Equal(2, controller.Pages.Count);
        Assert.False(controller.EndedWithCancel);
        Assert.Equal(Color.Black.ToArgb(), controller.Pages[1].GetPixel(2, 2).ToArgb());
    }

    /// <summary>
    /// A real three-page PDF goes through the whole job: Ok, three pages counted, three sheets, and
    /// each sheet carries the page's ink (the status alone is not trusted).
    /// </summary>
    [Fact]
    public void Prints_every_page_of_a_real_pdf_with_ink_on_each_sheet()
    {
        Assert.Equal(PdfLoadStatus.Ok, new PdfiumEngine().TryLoad(FixtureFactory.CreatePdf(3), null, out IPdfDocument? document));
        using (document)
        using (var controller = new FakePrintController())
        {
            PrintResult result = PrintJob.Run(document!, [0, 1, 2], TestPrinters.Settings(), "Three pages", ScaleMode.FitToPage, CancellationToken.None, controller);

            Assert.Equal(PrintStatus.Ok, result.Status);
            Assert.Equal(3, result.PagesPrinted);
            Assert.Equal(3, controller.Pages.Count);
            Assert.All(controller.Pages, sheet => Assert.True(Pixels.CountDark(sheet) > 50, "Expected the page's text on every sheet."));
            Assert.False(controller.EndedWithCancel);
        }
    }

    /// <summary>The pages print in the order given, repeats included, and the queue shows the document name.</summary>
    [Fact]
    public void Pages_print_in_the_order_given_under_the_document_name()
    {
        var document = FakePdfDocument.Letter(3);
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [2, 0, 2], TestPrinters.Settings(), "Packing slip 42", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.Equal(3, result.PagesPrinted);
        Assert.Equal([2, 0, 2], document.Calls.Select(call => call.PageIndex));
        Assert.Equal("Packing slip 42", controller.DocumentNameAtStart);
    }

    /// <summary>
    /// Cancelling between pages stops the job: the result is Cancelled and counts the pages already
    /// sent. The job ends normally rather than being aborted, so the pages counted really print.
    /// </summary>
    [Fact]
    public void Cancelled_token_returns_Cancelled_with_pages_already_sent()
    {
        using var cancellation = new CancellationTokenSource();

        // The token fires while the second page is being drawn, as a caller cancelling mid-job would.
        var document = new FakePdfDocument(Enumerable.Repeat(new SizeF(612, 792), 4).ToArray())
        {
            BeforeRender = pageIndex =>
            {
                if (pageIndex == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [0, 1, 2, 3], TestPrinters.Settings(), "Cancel me", ScaleMode.FitToPage, cancellation.Token, controller);

        Assert.Equal(PrintStatus.Cancelled, result.Status);
        Assert.Equal(Messages.Cancelled(), result.Message);
        Assert.Equal(2, result.PagesPrinted);
        Assert.Equal(2, controller.Pages.Count);
        Assert.All(controller.Pages, sheet => Assert.True(Pixels.CountDark(sheet) > 0, "Every sent page must carry its drawing; no blank sheet may be started."));
        Assert.False(controller.EndedWithCancel);
    }

    /// <summary>A token cancelled before the job starts prints nothing and opens no job at all.</summary>
    [Fact]
    public void Token_cancelled_before_the_start_prints_nothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(2), [0, 1], TestPrinters.Settings(), "Never", ScaleMode.FitToPage, cancellation.Token, controller);

        Assert.Equal(PrintStatus.Cancelled, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(0, controller.StartPrintCalls);
    }

    /// <summary>
    /// A token that fires after the job opened but before the first page is drawn aborts the job, so
    /// not even a blank sheet comes out.
    /// </summary>
    [Fact]
    public void Token_cancelled_as_the_job_opens_aborts_it_with_nothing_printed()
    {
        using var cancellation = new CancellationTokenSource();
        using var controller = new FakePrintController { OnStartPrintCallback = cancellation.Cancel };

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(2), [0, 1], TestPrinters.Settings(), "Abort", ScaleMode.FitToPage, cancellation.Token, controller);

        Assert.Equal(PrintStatus.Cancelled, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.True(controller.EndedWithCancel);
    }

    /// <summary>
    /// When the printer driver refuses device-context drawing, the page is rendered as a bitmap and
    /// drawn onto the sheet instead, and the job still succeeds with ink where the page belongs.
    /// </summary>
    [Fact]
    public void Driver_refusing_device_context_rendering_falls_back_to_bitmap()
    {
        var document = new FakePdfDocument(new SizeF(612, 792), new SizeF(612, 792)) { RefuseDeviceContext = true };
        using var controller = new FakePrintController(dpi: 100);

        PrintResult result = PrintJob.Run(document, [0, 1], TestPrinters.Settings(), "Fallback", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.Equal(2, result.PagesPrinted);
        Assert.Equal(
            [RenderKind.DeviceContext, RenderKind.Bitmap, RenderKind.DeviceContext, RenderKind.Bitmap],
            document.Calls.Select(call => call.Kind));

        // Letter on a Letter sheet fills it, so the bitmap is asked for at the sheet's 100 dpi.
        Assert.All(document.Calls.Where(call => call.Kind == RenderKind.Bitmap), call => Assert.Equal(100, call.Dpi));
        Assert.All(controller.Pages, sheet => Assert.Equal(Color.Black.ToArgb(), sheet.GetPixel(sheet.Width / 2, sheet.Height / 2).ToArgb()));
    }

    /// <summary>
    /// A real PDF printed through the bitmap fallback still carries its text on every sheet.
    /// </summary>
    [Fact]
    public void Bitmap_fallback_of_a_real_pdf_puts_ink_on_every_sheet()
    {
        Assert.Equal(PdfLoadStatus.Ok, new PdfiumEngine().TryLoad(FixtureFactory.CreatePdf(2), null, out IPdfDocument? real));
        using var document = new FakePdfDocument(real!) { RefuseDeviceContext = true };
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [0, 1], TestPrinters.Settings(), "Fallback", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.All(controller.Pages, sheet => Assert.True(Pixels.CountDark(sheet) > 50, "Expected the page's text on every sheet."));
    }

    /// <summary>
    /// A printer that cannot be opened when the job starts (deleted, unreachable, refused) makes the job fail fast with
    /// PrinterUnavailable and the standard message, instead of throwing or hanging.
    /// </summary>
    [Fact]
    public async Task Printer_offline_returns_PrinterUnavailable_without_hanging()
    {
        PrinterSettings settings = TestPrinters.Settings();
        using var controller = new FakePrintController { ThrowOnStartPrint = new InvalidPrinterException(settings) };

        PrintResult result = await Task.Run(
            () => PrintJob.Run(FakePdfDocument.Letter(2), [0, 1], settings, "Offline", ScaleMode.FitToPage, CancellationToken.None, controller))
            .WaitAsync(NoHang, TestContext.Current.CancellationToken);

        Assert.Equal(PrintStatus.PrinterUnavailable, result.Status);
        Assert.Equal(Messages.PrinterUnavailable(), result.Message);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(1, controller.StartPrintCalls);
    }

    /// <summary>A print system error when the job opens (StartDoc failing) is PrinterUnavailable too.</summary>
    [Fact]
    public void Print_system_error_at_the_start_returns_PrinterUnavailable()
    {
        using var controller = new FakePrintController { ThrowOnStartPrint = new Win32Exception(1801) };

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(1), [0], TestPrinters.Settings(), "Refused", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.PrinterUnavailable, result.Status);
        Assert.Equal(0, result.PagesPrinted);
    }

    /// <summary>
    /// A printer that fails part-way through reports PrinterUnavailable and counts no pages: the job
    /// ends aborted, and the print system withdraws an aborted job, so the pages drawn before the
    /// failure normally never come out.
    /// </summary>
    [Fact]
    public void Printer_failing_mid_job_returns_PrinterUnavailable_with_no_pages_counted()
    {
        using var controller = new FakePrintController { ThrowOnStartPage = new Win32Exception(), ThrowOnStartPageNumber = 3 };

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(4), [0, 1, 2, 3], TestPrinters.Settings(), "Jam", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.PrinterUnavailable, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.True(controller.EndedWithCancel);
    }

    /// <summary>
    /// A cancel mid-job ends the job normally, but if closing it then fails, the pages may never
    /// print: that is a failure with no pages counted, not a Cancelled result claiming two pages.
    /// </summary>
    [Fact]
    public void Cancelled_job_that_then_fails_to_close_is_a_failure_with_no_pages_counted()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new FakePdfDocument(Enumerable.Repeat(new SizeF(612, 792), 4).ToArray())
        {
            BeforeRender = pageIndex =>
            {
                if (pageIndex == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        using var controller = new FakePrintController { ThrowOnEndPrint = new Win32Exception() };

        PrintResult result = PrintJob.Run(document, [0, 1, 2, 3], TestPrinters.Settings(), "Cancel then fail", ScaleMode.FitToPage, cancellation.Token, controller);

        Assert.Equal(PrintStatus.PrinterUnavailable, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.False(controller.EndedWithCancel);
    }

    /// <summary>Any other exception from the print system becomes UnknownError, with its text for whoever looks after the program.</summary>
    [Fact]
    public void Unexpected_print_system_exception_becomes_UnknownError_with_its_details()
    {
        using var controller = new FakePrintController { ThrowOnStartPrint = new InvalidOperationException("Spooler said no in an unexpected way.") };

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(1), [0], TestPrinters.Settings(), "Odd", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.StartsWith(Messages.UnknownError(), result.Message);
        Assert.Contains("Spooler said no in an unexpected way.", result.Message);
    }

    /// <summary>
    /// The print system cancelling the job by itself (for example a Save As box closed without
    /// saving) is reported as Cancelled, never as Ok, because no page was sent.
    /// </summary>
    [Fact]
    public void Print_system_cancelling_the_job_is_Cancelled_not_Ok()
    {
        using var controller = new FakePrintController { CancelOnStartPrint = true };

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(2), [0, 1], TestPrinters.Settings(), "Closed", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Cancelled, result.Status);
        Assert.Equal(0, result.PagesPrinted);
    }

    /// <summary>
    /// A page that cannot be drawn either way stops the job with UnknownError naming the page. The job
    /// is aborted so no half-drawn sheet is printed, and since an aborted job is withdrawn from the
    /// queue, no pages are counted.
    /// </summary>
    [Fact]
    public void Page_that_cannot_be_drawn_stops_the_job_with_a_message_naming_it()
    {
        var document = new FakePdfDocument(Enumerable.Repeat(new SizeF(612, 792), 4).ToArray())
        {
            RefuseDeviceContext = true,
            BitmapFailure = pageIndex => pageIndex == 2 ? new InvalidOperationException("PDFium could not load page 3; the page may be damaged.") : null,
        };
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [0, 1, 2, 3], TestPrinters.Settings(), "Damaged", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.Equal(Messages.PageFailed(3, "PDFium could not load page 3; the page may be damaged."), result.Message);
        Assert.Equal(0, result.PagesPrinted);
        Assert.True(controller.EndedWithCancel);
    }

    /// <summary>
    /// A page too large for the bitmap fallback ends as UnknownError with a plain message about the
    /// page size, not a crash (the fallback may run out of memory, notably in 32-bit processes).
    /// </summary>
    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(ArgumentException))]
    public void Page_too_large_for_the_bitmap_fallback_returns_UnknownError_with_a_plain_message(Type failure)
    {
        var document = new FakePdfDocument(new SizeF(612, 792), new SizeF(14400, 14400))
        {
            RefuseDeviceContext = true,
            BitmapFailure = pageIndex => pageIndex == 1 ? (Exception)Activator.CreateInstance(failure)! : null,
        };
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [0, 1], TestPrinters.Settings(), "Poster", ScaleMode.ActualSize, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.UnknownError, result.Status);
        Assert.Equal(Messages.PageTooLarge(2), result.Message);
        Assert.Equal(0, result.PagesPrinted);
        Assert.True(controller.EndedWithCancel);
    }

    /// <summary>
    /// When the driver cannot make the copies itself (it reports a maximum below the request, as
    /// Microsoft Print to PDF does with 1), the whole document is repeated in software, in order, and
    /// the driver is asked for one copy only, so the result is three copies and not nine. The
    /// caller's settings are left as they were.
    /// </summary>
    [Fact]
    public void Copies_beyond_the_driver_maximum_repeat_the_document_in_software()
    {
        PrinterSettings settings = TestPrinters.Settings(copies: 3);
        Assert.Equal(1, settings.MaximumCopies);
        var document = FakePdfDocument.Letter(2);
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(document, [0, 1], settings, "Three copies", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.Equal(6, result.PagesPrinted);
        Assert.Equal(6, controller.Pages.Count);
        Assert.Equal([0, 1, 0, 1, 0, 1], document.Calls.Select(call => call.PageIndex));
        Assert.Equal((short)1, controller.CopiesAtStart);
        Assert.Equal(1, controller.StartPrintCalls);
        Assert.Equal(3, settings.Copies);
    }

    /// <summary>
    /// The copy plan: the driver makes the copies when it can; otherwise the document is repeated in
    /// software and the driver gets one copy. Never both, which would multiply the copies.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 999, 1, 1)]
    [InlineData(3, 999, 1, 3)]
    [InlineData(3, 3, 1, 3)]
    [InlineData(3, 2, 3, 1)]
    [InlineData(3, 1, 3, 1)]
    [InlineData(3, 0, 3, 1)]
    [InlineData(3, -1, 3, 1)]
    [InlineData(32767, 1, 32767, 1)]
    public void Copy_plan_lets_the_driver_copy_only_when_it_can(short requested, int driverMaximum, int expectedRepetitions, short expectedDriverCopies)
    {
        (int repetitions, short driverCopies) = PrintJob.PlanCopies(requested, driverMaximum);

        Assert.Equal(expectedRepetitions, repetitions);
        Assert.Equal(expectedDriverCopies, driverCopies);
        Assert.Equal(Math.Max((int)requested, 1), repetitions * driverCopies);
    }

    /// <summary>An empty page list starts no job (a job with no pages would still print a blank sheet).</summary>
    [Fact]
    public void Empty_page_list_prints_nothing_and_returns_InvalidPageRange()
    {
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(2), [], TestPrinters.Settings(), "Empty", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.InvalidPageRange, result.Status);
        Assert.Equal(0, controller.StartPrintCalls);
    }

    /// <summary>A page index the document does not have is PageNotFound, before any job starts.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Page_outside_the_document_returns_PageNotFound_before_printing(int badPage)
    {
        using var controller = new FakePrintController();

        PrintResult result = PrintJob.Run(FakePdfDocument.Letter(2), [0, badPage], TestPrinters.Settings(), "Bad page", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.PageNotFound, result.Status);
        Assert.Equal(0, controller.StartPrintCalls);
    }

    /// <summary>
    /// In a mixed document each page is laid out on its own: the landscape page in the middle is
    /// turned and fitted, and the portrait pages around it are not affected by it.
    /// </summary>
    [Fact]
    public void Mixed_size_document_lays_out_each_page_on_its_own()
    {
        var document = new FakePdfDocument(new SizeF(595.28f, 841.89f), new SizeF(792, 612), new SizeF(595.28f, 841.89f));
        using var controller = new FakePrintController(850, 1100, 100);

        PrintResult result = PrintJob.Run(document, [0, 1, 2], TestPrinters.Settings(), "Mixed", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.Ok, result.Status);
        RenderCall[] calls = document.Calls.ToArray();
        Assert.Equal(3, calls.Length);

        // A4 on Letter: limited by the height, centred across. Letter landscape: turned to fill the sheet.
        Assert.Equal(new Rectangle(36, 0, 778, 1100), calls[0].DestinationPixels);
        Assert.Equal(0, calls[0].QuarterTurns);
        Assert.Equal(new Rectangle(0, 0, 850, 1100), calls[1].DestinationPixels);
        Assert.Equal(3, calls[1].QuarterTurns);
        Assert.Equal(calls[0].DestinationPixels, calls[2].DestinationPixels);
        Assert.Equal(0, calls[2].QuarterTurns);
    }

    /// <summary>Missing arguments are a programming error inside the library, reported as UnknownError rather than thrown.</summary>
    [Fact]
    public void Missing_arguments_return_UnknownError_instead_of_throwing()
    {
        using var controller = new FakePrintController();

        PrintResult noDocument = PrintJob.Run(null!, [0], TestPrinters.Settings(), "x", ScaleMode.FitToPage, CancellationToken.None, controller);
        PrintResult noPages = PrintJob.Run(FakePdfDocument.Letter(1), null!, TestPrinters.Settings(), "x", ScaleMode.FitToPage, CancellationToken.None, controller);
        PrintResult noSettings = PrintJob.Run(FakePdfDocument.Letter(1), [0], null!, "x", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintStatus.UnknownError, noDocument.Status);
        Assert.Equal(PrintStatus.UnknownError, noPages.Status);
        Assert.Equal(PrintStatus.UnknownError, noSettings.Status);
        Assert.Equal(0, controller.StartPrintCalls);
    }

    /// <summary>A blank document name is replaced by a readable default for the print queue.</summary>
    [Fact]
    public void Blank_document_name_gets_a_default_queue_name()
    {
        using var controller = new FakePrintController();

        PrintJob.Run(FakePdfDocument.Letter(1), [0], TestPrinters.Settings(), "  ", ScaleMode.FitToPage, CancellationToken.None, controller);

        Assert.Equal(PrintJob.DefaultDocumentName, controller.DocumentNameAtStart);
    }
}
