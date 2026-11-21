using System.Drawing.Printing;
using Xunit;

namespace PDFStream.NET.Tests.Printing;

/// <summary>Printer settings for tests that must not touch any real printer.</summary>
internal static class TestPrinters
{
    /// <summary>The installed printer the tests that need real printer settings use. It is never printed to.</summary>
    public const string MicrosoftPrintToPdf = "Microsoft Print to PDF";

    /// <summary>
    /// Skips the calling test, with the reason shown in the test output, when "Microsoft Print to PDF"
    /// is not installed. The facade validates the printer against the real print system before it runs
    /// the job, so a test that reaches the job needs one real printer name (the job itself is faked).
    /// </summary>
    public static void RequireMicrosoftPrintToPdf()
    {
        bool installed = PDFStream.NET.Printing.SystemPrinterCatalog.Instance.GetInstalledPrinterNames()
            .Contains(MicrosoftPrintToPdf, StringComparer.OrdinalIgnoreCase);
        Assert.SkipUnless(installed, $"The '{MicrosoftPrintToPdf}' printer is not installed on this machine, so a print call cannot get as far as its job.");
    }

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
            PrinterName = "PDFStream.NET test printer (not installed) " + Guid.NewGuid().ToString("N"),
            Copies = copies,
        };
        settings.DefaultPageSettings.Landscape = false;
        settings.DefaultPageSettings.PaperSize = new PaperSize("Test sheet", 850, 1100);
        return settings;
    }
}
