using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using PDFStream.Engine;

namespace PDFStream.Printing;

/// <summary>
/// Prints chosen pages of an open PDF as one Windows print job, through <see cref="PrintDocument"/>
/// without a progress dialog. Every way the job can go wrong (printer offline, a page that cannot be
/// drawn, cancellation) comes back as a <see cref="PrintResult"/>; nothing is thrown.
/// </summary>
internal static class PrintJob
{
    /// <summary>The name shown in the print queue when the caller gives none.</summary>
    internal const string DefaultDocumentName = "PDF document";

    /// <summary>
    /// Prints the pages, each one laid out on its own sheet by <see cref="PagePainter"/>.
    /// </summary>
    /// <param name="doc">The open document. The caller keeps ownership.</param>
    /// <param name="pages">Zero-based page indexes, in print order; repeats are printed again.</param>
    /// <param name="settings">Ready printer settings (see <see cref="PrinterLocator"/>). When the driver
    /// cannot make the requested copies itself, the copy count is set to 1 for the job and restored
    /// afterwards, and the document is repeated instead.</param>
    /// <param name="documentName">The name shown in the print queue; blank means <see cref="DefaultDocumentName"/>.</param>
    /// <param name="scale">How each page is sized onto the paper.</param>
    /// <param name="token">Checked between pages. When it fires, the page being drawn is finished and the
    /// job ends there, so the pages counted as sent really print.</param>
    /// <param name="controller">Runs the job. <see langword="null"/> uses
    /// <see cref="StandardPrintController"/>, which prints without a progress dialog. Tests pass a fake
    /// that prints onto bitmaps.</param>
    /// <returns>
    /// <see cref="PrintStatus.Ok"/> only when every page was drawn and sent. Otherwise
    /// <see cref="PrintStatus.InvalidPageRange"/> (no pages), <see cref="PrintStatus.PageNotFound"/>,
    /// <see cref="PrintStatus.Cancelled"/>, <see cref="PrintStatus.PrinterUnavailable"/> or
    /// <see cref="PrintStatus.UnknownError"/>, with <see cref="PrintResult.PagesPrinted"/> set to the
    /// pages sent before the job stopped.
    /// </returns>
    /// <remarks>
    /// Software copies (when the driver's <see cref="PrinterSettings.MaximumCopies"/> is below the
    /// request, as with Microsoft Print to PDF) repeat the whole document in the same job, in order,
    /// so the copies come out collated and a print-to-file job still writes one file.
    /// <see cref="PrintResult.PagesPrinted"/> counts every page sent, across the repetitions; when the
    /// driver makes the copies, each page is sent once.
    /// </remarks>
    internal static PrintResult Run(
        IPdfDocument doc,
        IReadOnlyList<int> pages,
        PrinterSettings settings,
        string documentName,
        ScaleMode scale,
        CancellationToken token,
        PrintController? controller = null)
    {
        if (doc is null || pages is null || settings is null)
        {
            // The library's own callers always pass these, so this is a bug inside the library:
            // reported like any other failure rather than thrown at the caller.
            return PrintResult.Failure(PrintStatus.UnknownError, Messages.UnknownError("The print job was started without a document, a page list or printer settings."));
        }

        PageFeeder? feeder = null;
        short? copiesToRestore = null;
        try
        {
            if (pages.Count == 0)
            {
                // A print job without pages would still push one blank sheet out of the printer.
                return PrintResult.Failure(PrintStatus.InvalidPageRange);
            }

            int pageCount = doc.PageCount;
            foreach (int page in pages)
            {
                if ((uint)page >= (uint)pageCount)
                {
                    return PrintResult.Failure(PrintStatus.PageNotFound);
                }
            }

            if (token.IsCancellationRequested)
            {
                return PrintResult.Failure(PrintStatus.Cancelled);
            }

            // Asking the driver for its maximum is slow on some printers, so only when it matters.
            short requestedCopies = settings.Copies;
            int driverMaximum = requestedCopies > 1 ? settings.MaximumCopies : 1;
            (int repetitions, short driverCopies) = PlanCopies(requestedCopies, driverMaximum);
            if (driverCopies != requestedCopies)
            {
                copiesToRestore = requestedCopies;
                settings.Copies = driverCopies;
            }

            feeder = new PageFeeder(doc, pages, repetitions, scale, token);
            using var document = new PrintDocument
            {
                // PrinterSettings first: assigning it also adopts its page settings (colour, paper
                // source) for the job.
                PrinterSettings = settings,
                DocumentName = string.IsNullOrWhiteSpace(documentName) ? DefaultDocumentName : documentName,
                PrintController = controller ?? new StandardPrintController(),
            };
            document.PrintPage += feeder.PrintPage;
            document.Print();
            return feeder.Result();
        }
        catch (Exception ex)
        {
            return feeder is null ? MapException(ex, pagesSent: 0) : feeder.Failed(ex);
        }
        finally
        {
            // The settings belong to the caller; leave them as they came.
            if (copiesToRestore is short copies)
            {
                settings.Copies = copies;
            }
        }
    }

    /// <summary>
    /// Decides who makes the copies. The driver does when it says it can make that many; otherwise the
    /// document is repeated in software and the driver is asked for one copy. Never both: a driver
    /// asked for 3 copies of a document already repeated 3 times would print 9.
    /// </summary>
    /// <param name="requestedCopies">The copies the caller asked for.</param>
    /// <param name="driverMaximum">The driver's <see cref="PrinterSettings.MaximumCopies"/>. A value
    /// below 1 (the driver could not say) means it cannot be trusted with copies.</param>
    /// <returns>How many times to send the document, and the copy count to give the driver.</returns>
    internal static (int Repetitions, short DriverCopies) PlanCopies(short requestedCopies, int driverMaximum)
    {
        if (requestedCopies <= 1)
        {
            return (1, 1);
        }

        return driverMaximum >= requestedCopies ? (1, requestedCopies) : (requestedCopies, (short)1);
    }

    /// <summary>
    /// Turns an exception from the print system into a result: the printer could not be opened or
    /// refused the job (<see cref="InvalidPrinterException"/>, or a Windows error such as a failing
    /// StartDoc, which arrives as an <see cref="ExternalException"/>), or anything else.
    /// </summary>
    private static PrintResult MapException(Exception ex, int pagesSent) => ex switch
    {
        InvalidPrinterException or ExternalException => PrintResult.Failure(PrintStatus.PrinterUnavailable, pagesPrinted: pagesSent),
        _ => PrintResult.Failure(PrintStatus.UnknownError, Messages.UnknownError(ex.Message), pagesSent),
    };

    /// <summary>
    /// Feeds the pages to the print document one sheet at a time and remembers why the job stopped
    /// early, if it did. One instance serves one job.
    /// </summary>
    private sealed class PageFeeder(IPdfDocument doc, IReadOnlyList<int> pages, int repetitions, ScaleMode scale, CancellationToken token)
    {
        private readonly long _total = (long)pages.Count * repetitions;
        private long _next;
        private PrintResult? _stopped;

        /// <summary>Pages drawn and handed to the print system so far, across all repetitions.</summary>
        public int PagesSent { get; private set; }

        /// <summary>Handles the print document's page event: draws the next page and says whether more follow.</summary>
        public void PrintPage(object? sender, PrintPageEventArgs e)
        {
            // The print system has already started a sheet for this event. A cancel that arrives before
            // anything was drawn aborts the job: nothing has been sent yet, and ending normally would
            // push that blank sheet out of the printer.
            if (PagesSent == 0 && token.IsCancellationRequested)
            {
                _stopped = PrintResult.Failure(PrintStatus.Cancelled);
                e.Cancel = true;
                e.HasMorePages = false;
                return;
            }

            int pageIndex = pages[(int)(_next % pages.Count)];
            try
            {
                Graphics graphics = e.Graphics ?? throw new InvalidOperationException("The print system did not provide a drawing surface for the page.");
                RectangleF printableArea = PagePainter.PrepareSheet(graphics);
                if (printableArea.Width <= 0 || printableArea.Height <= 0)
                {
                    // A printer that reports no printable area cannot print anything; the fault is the
                    // printer's, not the page's.
                    Abort(e, PrintResult.Failure(PrintStatus.PrinterUnavailable, pagesPrinted: PagesSent));
                    return;
                }

                int printerDpi = (int)Math.Round(Math.Max(graphics.DpiX, graphics.DpiY));
                PagePainter.Paint(doc, pageIndex, graphics, printableArea, scale, printerDpi);
            }
            catch (PagePainter.PageTooLargeException)
            {
                Abort(e, PrintResult.Failure(PrintStatus.UnknownError, Messages.PageTooLarge(pageIndex + 1), PagesSent));
                return;
            }
            catch (Exception ex)
            {
                Abort(e, PrintResult.Failure(PrintStatus.UnknownError, Messages.PageFailed(pageIndex + 1, ex.Message), PagesSent));
                return;
            }

            PagesSent++;
            _next++;

            // Cancellation is checked once a page is complete, never by leaving a started sheet blank.
            // Stopping here ends the job normally, so every page counted as sent really prints.
            if (_next < _total && token.IsCancellationRequested)
            {
                _stopped = PrintResult.Failure(PrintStatus.Cancelled, pagesPrinted: PagesSent);
                e.HasMorePages = false;
                return;
            }

            e.HasMorePages = _next < _total;
        }

        /// <summary>The result of a job the print system ran to its end.</summary>
        public PrintResult Result()
        {
            if (_stopped is not null)
            {
                return _stopped;
            }

            // Fewer pages than asked for, with no reason recorded here, means the print system ended the
            // job by itself (for example a Save As box closed without saving). That is never success.
            return PagesSent == _total
                ? PrintResult.Success(PagesSent)
                : PrintResult.Failure(PrintStatus.Cancelled, pagesPrinted: PagesSent);
        }

        /// <summary>The result of a job the print system ended with an exception.</summary>
        public PrintResult Failed(Exception ex) =>

            // A reason a page recorded wins over whatever the print system threw while closing the job.
            _stopped ?? MapException(ex, PagesSent);

        /// <summary>
        /// Stops the job because of a failure. The job is aborted, so the print system withdraws it
        /// from the queue and the sheet started for this page (blank or half drawn) never comes out.
        /// </summary>
        private void Abort(PrintPageEventArgs e, PrintResult why)
        {
            _stopped = why;
            e.Cancel = true;
            e.HasMorePages = false;
        }
    }
}
