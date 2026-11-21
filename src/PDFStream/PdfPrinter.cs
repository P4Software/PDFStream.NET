using System.Drawing.Printing;
using PDFStream.Engine;
using PDFStream.Printing;

namespace PDFStream;

/// <summary>
/// Runs one print job on a Windows printer. This is the delegate form of <see cref="PrintJob.Run"/>
/// without its optional print controller, so tests can replace the job and the facade can be checked
/// without a printer.
/// </summary>
/// <param name="doc">The open document.</param>
/// <param name="pages">Zero-based page indexes, in print order.</param>
/// <param name="settings">Ready printer settings.</param>
/// <param name="documentName">The name for the print queue; may be blank.</param>
/// <param name="scale">How each page is sized onto the paper.</param>
/// <param name="token">Cancels the job between pages.</param>
/// <returns>What the job did.</returns>
internal delegate PrintResult PrintJobRunner(
    IPdfDocument doc,
    IReadOnlyList<int> pages,
    PrinterSettings settings,
    string documentName,
    ScaleMode scale,
    CancellationToken token);

/// <summary>
/// Silently prints a PDF on a Windows printer: no dialog, no PDF viewer and no command line. Pass the PDF
/// as a file path, a stream or a byte array together with the <see cref="PrintOptions"/>, and read the
/// returned <see cref="PrintResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// None of the methods throws for bad input or printer problems (a missing file, a damaged or protected
/// PDF, a wrong printer name, a paused printer, a bad page range, even an unexpected internal failure):
/// each returns a <see cref="PrintResult"/> whose <see cref="PrintResult.Message"/> says in plain language
/// what happened and what to do next.
/// </para>
/// <para>
/// The steps run in this order, and the first problem ends the call: the options are checked, the PDF is
/// opened, the page range is checked against the real page count, the printer and its settings are
/// prepared, and then the job runs. A PDF that is protected only by an owner password (restrictions but
/// no open password) prints without any password.
/// </para>
/// <para>
/// PDFium, the engine that reads and draws the PDF, is not thread-safe, so every call into it is made under
/// one lock for the whole process. The lock is held for a single engine call (opening the PDF, measuring or
/// drawing one page, closing it), not for a whole job. The methods may therefore be called from several
/// threads at once: the jobs run side by side and their engine work takes turns, so they interleave at page
/// level and each takes longer than it would alone. Each call is still its own print job with its own pages
/// in their own order, and no call fails because another one is running.
/// </para>
/// </remarks>
public static class PdfPrinter
{
    /// <summary>The job runner used outside the tests: the real print job.</summary>
    private static readonly PrintJobRunner RealJob = (doc, pages, settings, documentName, scale, token) =>
        PrintJob.Run(doc, pages, settings, documentName, scale, token);

    /// <summary>Opens the PDF with the password from the options; what it returns is owned by the caller.</summary>
    private delegate PrintStatus DocumentLoader(string? password, out IPdfDocument? document);

    /// <summary>Prints a PDF file.</summary>
    /// <param name="path">The path of the PDF file. A missing, unreadable or blank path returns <see cref="PrintStatus.FileNotFound"/>.</param>
    /// <param name="options">What and where to print. <see langword="null"/> returns <see cref="PrintStatus.InvalidOptions"/>.</param>
    /// <param name="cancellationToken">
    /// Stops the job. It is checked before the PDF is opened and between pages; a job stopped between
    /// pages ends normally, so the pages already sent do print and <see cref="PrintResult.PagesPrinted"/> counts them.
    /// </param>
    /// <returns>
    /// The outcome, never an exception. <see cref="PrintResult.Succeeded"/> is <see langword="true"/> only
    /// when every requested page was rendered and sent to the printer.
    /// </returns>
    /// <remarks>
    /// Safe to call from several threads at once. PDFium work is serialised one engine call at a time across the
    /// whole process, so concurrent calls run side by side and take turns page by page; each stays its own print job.
    /// </remarks>
    public static PrintResult Print(string path, PrintOptions options, CancellationToken cancellationToken = default) =>
        Print(path, options, new PdfiumEngine(), SystemPrinterCatalog.Instance, RealJob, cancellationToken);

    /// <summary>Prints a PDF read from a stream.</summary>
    /// <param name="pdf">
    /// A readable stream holding the whole PDF, read from its current position to its end. It does not need
    /// to support seeking and is left open: it belongs to the caller. <see langword="null"/>, an empty or an
    /// unreadable stream returns <see cref="PrintStatus.InvalidPdf"/>.
    /// </param>
    /// <param name="options">What and where to print. <see langword="null"/> returns <see cref="PrintStatus.InvalidOptions"/>.</param>
    /// <param name="cancellationToken">
    /// Stops the job. It is checked before the PDF is opened and between pages; a job stopped between
    /// pages ends normally, so the pages already sent do print and <see cref="PrintResult.PagesPrinted"/> counts them.
    /// </param>
    /// <returns>
    /// The outcome, never an exception. <see cref="PrintResult.Succeeded"/> is <see langword="true"/> only
    /// when every requested page was rendered and sent to the printer.
    /// </returns>
    /// <remarks>
    /// Safe to call from several threads at once. PDFium work is serialised one engine call at a time across the
    /// whole process, so concurrent calls run side by side and take turns page by page; each stays its own print job.
    /// </remarks>
    public static PrintResult Print(Stream pdf, PrintOptions options, CancellationToken cancellationToken = default) =>
        Print(pdf, options, new PdfiumEngine(), SystemPrinterCatalog.Instance, RealJob, cancellationToken);

    /// <summary>Prints a PDF held in memory.</summary>
    /// <param name="pdf">The complete PDF file. <see langword="null"/> or empty returns <see cref="PrintStatus.InvalidPdf"/>. The array is not changed or kept.</param>
    /// <param name="options">What and where to print. <see langword="null"/> returns <see cref="PrintStatus.InvalidOptions"/>.</param>
    /// <param name="cancellationToken">
    /// Stops the job. It is checked before the PDF is opened and between pages; a job stopped between
    /// pages ends normally, so the pages already sent do print and <see cref="PrintResult.PagesPrinted"/> counts them.
    /// </param>
    /// <returns>
    /// The outcome, never an exception. <see cref="PrintResult.Succeeded"/> is <see langword="true"/> only
    /// when every requested page was rendered and sent to the printer.
    /// </returns>
    /// <remarks>
    /// Safe to call from several threads at once. PDFium work is serialised one engine call at a time across the
    /// whole process, so concurrent calls run side by side and take turns page by page; each stays its own print job.
    /// </remarks>
    public static PrintResult Print(byte[] pdf, PrintOptions options, CancellationToken cancellationToken = default) =>
        Print(pdf, options, new PdfiumEngine(), SystemPrinterCatalog.Instance, RealJob, cancellationToken);

    /// <summary>The file overload with the engine, the printer list and the job replaceable (for tests).</summary>
    internal static PrintResult Print(
        string path,
        PrintOptions options,
        IPdfEngine engine,
        IPrinterCatalog catalog,
        PrintJobRunner runner,
        CancellationToken cancellationToken = default) =>
        Execute(options, catalog, runner, cancellationToken, (string? password, out IPdfDocument? document) =>
            PdfSource.Load(engine, path, password, out document));

    /// <summary>The stream overload with the engine, the printer list and the job replaceable (for tests).</summary>
    internal static PrintResult Print(
        Stream pdf,
        PrintOptions options,
        IPdfEngine engine,
        IPrinterCatalog catalog,
        PrintJobRunner runner,
        CancellationToken cancellationToken = default) =>
        Execute(options, catalog, runner, cancellationToken, (string? password, out IPdfDocument? document) =>
            PdfSource.Load(engine, pdf, password, out document));

    /// <summary>The byte-array overload with the engine, the printer list and the job replaceable (for tests).</summary>
    internal static PrintResult Print(
        byte[] pdf,
        PrintOptions options,
        IPdfEngine engine,
        IPrinterCatalog catalog,
        PrintJobRunner runner,
        CancellationToken cancellationToken = default) =>
        Execute(options, catalog, runner, cancellationToken, (string? password, out IPdfDocument? document) =>
            PdfSource.Load(engine, pdf, password, out document));

    /// <summary>
    /// The steps every overload shares, in the order that reports the cheapest-to-fix problem first:
    /// options, cancellation, load, page range, printer settings, job. Nothing escapes as an exception.
    /// </summary>
    private static PrintResult Execute(
        PrintOptions options,
        IPrinterCatalog catalog,
        PrintJobRunner runner,
        CancellationToken cancellationToken,
        DocumentLoader load)
    {
        IPdfDocument? document = null;
        try
        {
            // Options first: a wrong option should fail the same way whatever the file is, and before
            // any file or printer is touched.
            if (!PrinterLocator.AreOptionsUsable(options))
            {
                return PrintResult.Failure(PrintStatus.InvalidOptions);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PrintResult.Failure(PrintStatus.Cancelled);
            }

            PrintStatus loaded = load(options.Password, out document);
            if (loaded != PrintStatus.Ok || document is null)
            {
                return PrintResult.Failure(loaded == PrintStatus.Ok ? PrintStatus.InvalidPdf : loaded);
            }

            // Opening a big file can take a while, so a cancellation that arrived meanwhile is honoured
            // before the printer is touched.
            if (cancellationToken.IsCancellationRequested)
            {
                return PrintResult.Failure(PrintStatus.Cancelled);
            }

            // The range is checked against the real page count, which is only known now.
            if (!PageRangeParser.TryParse(options.Pages, document.PageCount, out IReadOnlyList<int> pages, out PrintStatus rangeError))
            {
                return PrintResult.Failure(rangeError);
            }

            // The overload with a message, so the cause-specific text (paused queue, the list of paper
            // trays, a stopped Print Spooler) reaches the caller instead of the generic one.
            PrintStatus prepared = PrinterLocator.TryCreateSettings(options, catalog, out PrinterSettings? settings, out string message);
            if (prepared != PrintStatus.Ok || settings is null)
            {
                return PrintResult.Failure(prepared == PrintStatus.Ok ? PrintStatus.PrinterUnavailable : prepared, message);
            }

            // The job's result is passed on as it is: it already counts 0 pages after any failure and the
            // exact number after a cancellation that ended the job normally. A null from a replaced job
            // runner is a bug in that runner and is reported like any other unexpected failure.
            return runner(document, pages, settings, options.DocumentName ?? string.Empty, options.Scale, cancellationToken)
                ?? PrintResult.Failure(PrintStatus.UnknownError, Messages.UnknownError("The print job returned no result."));
        }
        catch (Exception ex)
        {
            // The promise of this class: no exception reaches the caller. The text of the exception is added
            // for the person who looks after the program; an empty message falls back to the type name.
            string details = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            return PrintResult.Failure(PrintStatus.UnknownError, Messages.UnknownError(details));
        }
        finally
        {
            Release(document);
        }
    }

    /// <summary>
    /// Disposes the document on every path. A failure while releasing is swallowed: the outcome of the job
    /// is already decided, and an exception thrown from here would replace the result the caller is owed.
    /// </summary>
    private static void Release(IPdfDocument? document)
    {
        try
        {
            document?.Dispose();
        }
        catch (Exception)
        {
            // Nothing useful can be done with a failed release; the result stands.
        }
    }
}
