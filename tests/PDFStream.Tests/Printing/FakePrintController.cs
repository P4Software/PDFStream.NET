using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;

namespace PDFStream.Tests.Printing;

/// <summary>
/// A print controller that "prints" onto in-memory bitmaps instead of a printer, so print jobs can be
/// tested without any printer, driver or spooler. Each page becomes one white 24-bit bitmap the size
/// of the sheet, which the tests then inspect pixel by pixel.
/// </summary>
/// <remarks>
/// <para>
/// It deliberately does not call <c>base.OnStartPrint</c>: that asks the printer driver for its
/// settings (a DEVMODE) and fails for a printer that is not installed. Without it, System.Drawing still
/// builds each page's event arguments, as long as the page settings name the paper size and the
/// orientation explicitly (see <see cref="TestPrinters.Settings"/>), because then it never needs the driver.
/// </para>
/// <para>
/// 24 bits per pixel on purpose: GDI drawing (the device-context path) leaves the alpha byte of 32-bit
/// pixels at zero, which would make drawn pixels look transparent.
/// </para>
/// </remarks>
internal sealed class FakePrintController : PrintController, IDisposable
{
    private readonly int _sheetWidth;
    private readonly int _sheetHeight;
    private readonly int _dpi;
    private readonly List<Bitmap> _pages = [];

    /// <summary>Creates a controller whose sheets have the given size and resolution.</summary>
    /// <param name="sheetWidth">Sheet width in hundredths of an inch (850 is US Letter).</param>
    /// <param name="sheetHeight">Sheet height in hundredths of an inch (1100 is US Letter).</param>
    /// <param name="dpi">Resolution of the sheet bitmaps, which the job sees as the printer resolution.</param>
    public FakePrintController(int sheetWidth = 850, int sheetHeight = 1100, int dpi = 100)
    {
        _sheetWidth = sheetWidth;
        _sheetHeight = sheetHeight;
        _dpi = dpi;
    }

    /// <summary>One bitmap per page that was started, in order.</summary>
    public IReadOnlyList<Bitmap> Pages => _pages;

    /// <summary>How many times a job was started.</summary>
    public int StartPrintCalls { get; private set; }

    /// <summary>The copy count the "driver" was asked for when the job started.</summary>
    public short? CopiesAtStart { get; private set; }

    /// <summary>The name the job was given for the print queue.</summary>
    public string? DocumentNameAtStart { get; private set; }

    /// <summary>
    /// Whether the job ended as cancelled (a real controller then aborts the spooled job and nothing
    /// prints) rather than normally; <see langword="null"/> when the job never reached its end.
    /// </summary>
    public bool? EndedWithCancel { get; private set; }

    /// <summary>When set, starting the job throws this, like a printer that cannot be opened or was deleted.</summary>
    public Exception? ThrowOnStartPrint { get; init; }

    /// <summary>
    /// When <see langword="true"/>, starting the job cancels it, as the real controller does when the
    /// print system reports the job cancelled (for example a Save As box closed without saving).
    /// </summary>
    public bool CancelOnStartPrint { get; init; }

    /// <summary>Runs when the job starts, before any page (for example to cancel a token at that moment).</summary>
    public Action? OnStartPrintCallback { get; init; }

    /// <summary>When set, closing the job throws this, like EndDoc or AbortDoc failing in the print system.</summary>
    public Exception? ThrowOnEndPrint { get; init; }

    /// <summary>When set together with <see cref="ThrowOnStartPageNumber"/>, starting that page throws this.</summary>
    public Exception? ThrowOnStartPage { get; init; }

    /// <summary>The 1-based number of the page whose start throws <see cref="ThrowOnStartPage"/>.</summary>
    public int ThrowOnStartPageNumber { get; init; }

    /// <inheritdoc/>
    public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
        StartPrintCalls++;
        CopiesAtStart = document.PrinterSettings.Copies;
        DocumentNameAtStart = document.DocumentName;
        OnStartPrintCallback?.Invoke();
        if (ThrowOnStartPrint is not null)
        {
            throw ThrowOnStartPrint;
        }

        e.Cancel = CancelOnStartPrint;
    }

    /// <inheritdoc/>
    public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
    {
        if (ThrowOnStartPage is not null && _pages.Count + 1 == ThrowOnStartPageNumber)
        {
            throw ThrowOnStartPage;
        }

        var sheet = new Bitmap(ToPixels(_sheetWidth), ToPixels(_sheetHeight), PixelFormat.Format24bppRgb);
        sheet.SetResolution(_dpi, _dpi);
        _pages.Add(sheet);

        // The print loop disposes this Graphics when the page ends; the bitmap stays for inspection.
        Graphics graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.White);
        return graphics;
    }

    /// <inheritdoc/>
    public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
        EndedWithCancel = e.Cancel;
        base.OnEndPrint(document, e);
        if (ThrowOnEndPrint is not null)
        {
            throw ThrowOnEndPrint;
        }
    }

    /// <summary>Releases the page bitmaps.</summary>
    public void Dispose()
    {
        foreach (Bitmap page in _pages)
        {
            page.Dispose();
        }
    }

    private int ToPixels(int hundredths) => (int)Math.Round(hundredths * _dpi / 100.0);
}
