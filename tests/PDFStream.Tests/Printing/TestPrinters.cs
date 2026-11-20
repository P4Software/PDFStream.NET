using System.Drawing.Printing;

namespace PDFStream.Tests.Printing;

/// <summary>Printer settings for tests that must not touch any real printer.</summary>
internal static class TestPrinters
{
    /// <summary>
    /// Settings for a printer that is not installed, usable with <see cref="FakePrintController"/>.
    /// </summary>
    /// <remarks>
    /// The paper size and orientation are set explicitly because System.Drawing otherwise asks the
    /// printer driver for them when it builds each page's event arguments, and there is no driver.
    /// For such a printer <see cref="PrinterSettings.MaximumCopies"/> reports 1, System.Drawing's
    /// fallback when the driver cannot be asked.
    /// </remarks>
    /// <param name="copies">The copy count the caller asked for.</param>
    public static PrinterSettings Settings(short copies = 1)
    {
        var settings = new PrinterSettings
        {
            PrinterName = "PDFStream test printer (not installed) " + Guid.NewGuid().ToString("N"),
            Copies = copies,
        };
        settings.DefaultPageSettings.Landscape = false;
        settings.DefaultPageSettings.PaperSize = new PaperSize("Test sheet", 850, 1100);
        return settings;
    }
}
