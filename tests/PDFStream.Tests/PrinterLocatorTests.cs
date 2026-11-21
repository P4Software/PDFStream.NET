using System.Drawing.Printing;
using PDFStream.Printing;
using PDFStream.Tests.Printing;
using Xunit;

namespace PDFStream.Tests;

/// <summary>
/// Tests <see cref="PrinterLocator"/>: option validation, printer-name matching and the settings
/// that end up on the printer. Name matching and the unknown-printer path use a fake printer list,
/// so they run anywhere. The tests that need a real printer use "Microsoft Print to PDF" and skip,
/// with the reason shown in the test output, when it is not installed. None of them prints,
/// opens a dialog or writes a file.
/// </summary>
public sealed class PrinterLocatorTests
{
    private const string PdfPrinter = "Microsoft Print to PDF";

    /// <summary>A printer list that returns a fixed set of names, or fails like a stopped spooler.</summary>
    private sealed class FakeCatalog(params string[] names) : IPrinterCatalog
    {
        public bool Fails { get; init; }

        public IReadOnlyList<string> GetInstalledPrinterNames() =>
            Fails ? throw new InvalidOperationException("The Print Spooler service is not running.") : names;
    }

    /// <summary>Skips the calling test, visibly, when the Microsoft Print to PDF printer is missing.</summary>
    private static void RequirePdfPrinter()
    {
        bool installed = SystemPrinterCatalog.Instance.GetInstalledPrinterNames()
            .Contains(PdfPrinter, StringComparer.OrdinalIgnoreCase);
        Assert.SkipUnless(installed, $"The '{PdfPrinter}' printer is not installed on this machine, so the real-printer settings cannot be checked.");
    }

    /// <summary>An empty or blank printer name is InvalidOptions and never reaches the print system.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_printer_name_is_InvalidOptions(string name)
    {
        var options = new PrintOptions { PrinterName = name };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, new FakeCatalog("Any"), out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.InvalidOptions, status);
        Assert.Null(settings);
        Assert.Equal(Messages.InvalidOptions(), message);
    }

    /// <summary>The two-argument form that later tasks call gives the same answer.</summary>
    [Fact]
    public void Two_argument_overload_reports_InvalidOptions_too()
    {
        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions(), out PrinterSettings? settings);

        Assert.Equal(PrintStatus.InvalidOptions, status);
        Assert.Null(settings);
    }

    /// <summary>Null options are a bad input like any other, not a NullReferenceException.</summary>
    [Fact]
    public void Null_options_are_InvalidOptions()
    {
        PrintStatus status = PrinterLocator.TryCreateSettings(null!, new FakeCatalog(), out PrinterSettings? settings, out _);

        Assert.Equal(PrintStatus.InvalidOptions, status);
        Assert.Null(settings);
    }

    /// <summary>Zero, negative and too many copies are InvalidOptions.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(32768)]
    public void Zero_copies_is_InvalidOptions(int copies)
    {
        var options = new PrintOptions { PrinterName = "Any", Copies = copies };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, new FakeCatalog("Any"), out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.InvalidOptions, status);
        Assert.Null(settings);
        Assert.Equal(Messages.InvalidOptions(), message);
    }

    /// <summary>A name no installed printer has is PrinterNotFound, with the standard message.</summary>
    [Fact]
    public void Unknown_printer_is_PrinterNotFound()
    {
        var options = new PrintOptions { PrinterName = "No such printer " + Guid.NewGuid().ToString("N") };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, new FakeCatalog("Office Laser", "Front Desk"), out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.PrinterNotFound, status);
        Assert.Null(settings);
        Assert.Equal(Messages.PrinterNotFound(), message);
    }

    /// <summary>Against the real print system an unknown name is PrinterNotFound too (no fake involved).</summary>
    [Fact]
    public void Unknown_printer_on_the_real_system_is_PrinterNotFound()
    {
        var options = new PrintOptions { PrinterName = "No such printer " + Guid.NewGuid().ToString("N") };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.PrinterNotFound, status);
        Assert.Null(settings);
    }

    /// <summary>A print system that cannot be read (spooler stopped) is PrinterUnavailable, not an exception.</summary>
    [Fact]
    public void Unreadable_print_system_is_PrinterUnavailable()
    {
        var options = new PrintOptions { PrinterName = "Any" };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, new FakeCatalog { Fails = true }, out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, status);
        Assert.Null(settings);
        Assert.Equal(Messages.PrinterUnavailable(), message);
    }

    /// <summary>
    /// A name that is in the list but that Windows cannot open (a fake entry here) is
    /// PrinterUnavailable, distinct from "not installed".
    /// </summary>
    [Fact]
    public void Listed_but_unopenable_printer_is_PrinterUnavailable()
    {
        string ghost = "Ghost printer " + Guid.NewGuid().ToString("N");
        var options = new PrintOptions { PrinterName = ghost };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, new FakeCatalog(ghost), out PrinterSettings? settings, out _);

        Assert.Equal(PrintStatus.PrinterUnavailable, status);
        Assert.Null(settings);
    }

    /// <summary>
    /// Name matching ignores letter case and surrounding spaces, handles non-ASCII letters and
    /// accepts the network form, and always returns the name exactly as the list spells it.
    /// </summary>
    [Theory]
    [InlineData("Microsoft Print to PDF", "microsoft print to pdf", "Microsoft Print to PDF")]
    [InlineData("Microsoft Print to PDF", "MICROSOFT PRINT TO PDF", "Microsoft Print to PDF")]
    [InlineData("Microsoft Print to PDF", "  Microsoft Print to PDF  ", "Microsoft Print to PDF")]
    [InlineData("Etiquetas Almac\u00E9n", "ETIQUETAS ALMAC\u00C9N", "Etiquetas Almac\u00E9n")]
    [InlineData("\u30D7\u30EA\u30F3\u30BF\u30FC 1", "\u30D7\u30EA\u30F3\u30BF\u30FC 1", "\u30D7\u30EA\u30F3\u30BF\u30FC 1")]
    [InlineData(@"\\SERVER\Label Printer", @"\\server\label printer", @"\\SERVER\Label Printer")]
    [InlineData(@"\\server\printer", @"\\server\printer", @"\\server\printer")]
    public void Printer_name_match_is_case_insensitive_and_accepts_network_form(string installed, string requested, string expected)
    {
        var catalog = new FakeCatalog("Other printer", installed, "Another one");

        string? found = PrinterLocator.FindInstalledName(catalog, requested);

        Assert.Equal(expected, found);
    }

    /// <summary>Names that only look alike do not match: a different server, a missing part, a changed space.</summary>
    [Theory]
    [InlineData(@"\\server2\printer")]
    [InlineData(@"server\printer")]
    [InlineData("MicrosoftPrint to PDF")]
    [InlineData("Microsoft Print")]
    [InlineData("Microsoft  Print to PDF")]
    public void Printer_name_match_does_not_guess(string requested)
    {
        var catalog = new FakeCatalog(@"\\server\printer", "Microsoft Print to PDF");

        string? found = PrinterLocator.FindInstalledName(catalog, requested);

        Assert.Null(found);
    }

    /// <summary>When two names differ only by case, the exact spelling wins.</summary>
    [Fact]
    public void Exact_spelling_beats_case_insensitive_match()
    {
        var catalog = new FakeCatalog("label printer", "Label Printer");

        Assert.Equal("Label Printer", PrinterLocator.FindInstalledName(catalog, "Label Printer"));
        Assert.Equal("label printer", PrinterLocator.FindInstalledName(catalog, "label printer"));
    }

    /// <summary>The duplex choices map to the Windows ones; the default maps to "change nothing".</summary>
    [Theory]
    [InlineData(DuplexMode.PrinterDefault, Duplex.Default)]
    [InlineData(DuplexMode.None, Duplex.Simplex)]
    [InlineData(DuplexMode.LongEdge, Duplex.Vertical)]
    [InlineData(DuplexMode.ShortEdge, Duplex.Horizontal)]
    public void Duplex_mode_maps_to_the_windows_value(DuplexMode mode, Duplex expected)
    {
        Assert.Equal(expected, PrinterLocator.ToSystemDuplex(mode));
    }

    /// <summary>
    /// Copies and colour reach the real printer settings, and the printer name is the installed
    /// spelling even when the caller typed it in another case.
    /// </summary>
    [Fact]
    public void Existing_printer_gets_copies_duplex_color_applied()
    {
        RequirePdfPrinter();
        var options = new PrintOptions
        {
            PrinterName = PdfPrinter.ToUpperInvariant(),
            Copies = 3,
            Color = false,
            Duplex = DuplexMode.None,
        };

        PrintStatus status = PrinterLocator.TryCreateSettings(options, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.Equal(PdfPrinter, settings.PrinterName);
        Assert.Equal(3, settings.Copies);
        Assert.False(settings.DefaultPageSettings.Color);
        Assert.False(settings.PrintToFile);
    }

    /// <summary>Colour is on by default and is applied as colour.</summary>
    [Fact]
    public void Color_defaults_to_color()
    {
        RequirePdfPrinter();

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter }, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.Equal(1, settings.Copies);
        Assert.True(settings.DefaultPageSettings.Color);
    }

    /// <summary>
    /// Double-sided printing on a printer that cannot do it is PrinterUnavailable, not a silent
    /// single-sided printout. Microsoft Print to PDF cannot duplex.
    /// </summary>
    [Theory]
    [InlineData(DuplexMode.LongEdge)]
    [InlineData(DuplexMode.ShortEdge)]
    public void Duplex_on_a_printer_that_cannot_duplex_is_PrinterUnavailable(DuplexMode mode)
    {
        RequirePdfPrinter();
        Assert.SkipWhen(new PrinterSettings { PrinterName = PdfPrinter }.CanDuplex, $"'{PdfPrinter}' can duplex on this machine, so the refusal path cannot be shown.");

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter, Duplex = mode }, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.PrinterUnavailable, status);
        Assert.Null(settings);
    }

    /// <summary>An output file turns on print to file with the full path, without creating the file.</summary>
    [Fact]
    public void OutputFile_sets_PrintToFile()
    {
        RequirePdfPrinter();
        string path = Path.Combine(Path.GetTempPath(), "pdfstream-" + Guid.NewGuid().ToString("N") + ".pdf");

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter, OutputFile = path }, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.True(settings.PrintToFile);
        Assert.Equal(path, settings.PrintFileName);
        Assert.False(File.Exists(path), "Preparing settings must not write the output file.");
    }

    /// <summary>A relative output path is anchored to the current folder so a service's own working folder never surprises anyone.</summary>
    [Fact]
    public void Relative_OutputFile_becomes_a_full_path()
    {
        RequirePdfPrinter();
        string name = "pdfstream-" + Guid.NewGuid().ToString("N") + ".pdf";

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter, OutputFile = name }, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.Equal(Path.GetFullPath(name), settings.PrintFileName);
        Assert.False(File.Exists(settings.PrintFileName));
    }

    /// <summary>An output file in a folder that does not exist, or that names a folder, is InvalidOptions.</summary>
    [Fact]
    public void Unusable_OutputFile_is_InvalidOptions()
    {
        string missingFolder = Path.Combine(Path.GetTempPath(), "pdfstream-" + Guid.NewGuid().ToString("N"), "out.pdf");

        foreach (string bad in new[] { missingFolder, Path.GetTempPath(), "   ", "bad|name?.pdf" })
        {
            PrintStatus status = PrinterLocator.TryCreateSettings(
                new PrintOptions { PrinterName = "Any", OutputFile = bad }, new FakeCatalog("Any"), out PrinterSettings? settings, out string message);

            Assert.Equal(PrintStatus.InvalidOptions, status);
            Assert.Null(settings);
            Assert.Equal(Messages.InvalidOutputFile(bad), message);
        }
    }

    /// <summary>
    /// A paper source the printer does not have is InvalidOptions, and the message names the
    /// printer's real sources so the caller can pick one.
    /// </summary>
    [Fact]
    public void Unknown_paper_source_is_InvalidOptions_listing_the_available_sources()
    {
        RequirePdfPrinter();
        string[] real = new PrinterSettings { PrinterName = PdfPrinter }.PaperSources.Cast<PaperSource>().Select(s => s.SourceName).ToArray();

        PrintStatus status = PrinterLocator.TryCreateSettings(
            new PrintOptions { PrinterName = PdfPrinter, PaperSource = "Tray that does not exist" }, SystemPrinterCatalog.Instance, out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.InvalidOptions, status);
        Assert.Null(settings);
        Assert.Contains("Tray that does not exist", message);
        Assert.Equal(Messages.UnknownPaperSource("Tray that does not exist", real), message);
        foreach (string source in real)
        {
            Assert.Contains(source, message);
        }
    }

    /// <summary>The paper source message lists every source, or says there are none.</summary>
    [Fact]
    public void Unknown_paper_source_message_lists_sources_or_says_there_are_none()
    {
        string withSources = Messages.UnknownPaperSource("Tray 9", ["Tray 1", "Manual Feed"]);
        string without = Messages.UnknownPaperSource("Tray 9", []);

        Assert.Contains("Tray 9", withSources);
        Assert.Contains("Tray 1", withSources);
        Assert.Contains("Manual Feed", withSources);
        Assert.Contains("Tray 9", without);
        Assert.Contains("does not report any", without);
    }

    /// <summary>A known paper source is matched ignoring case and applied to the page settings.</summary>
    [Fact]
    public void Known_paper_source_is_applied_ignoring_case()
    {
        // Microsoft Print to PDF has no sources at all, so use the XPS writer, which lists
        // "Automatically Select". Skips when it is not there.
        const string xps = "Microsoft XPS Document Writer";
        Assert.SkipUnless(
            SystemPrinterCatalog.Instance.GetInstalledPrinterNames().Contains(xps, StringComparer.OrdinalIgnoreCase),
            $"The '{xps}' printer is not installed on this machine.");
        string first = new PrinterSettings { PrinterName = xps }.PaperSources.Cast<PaperSource>().Select(s => s.SourceName).FirstOrDefault()
            ?? "";
        Assert.SkipWhen(first.Length == 0, $"'{xps}' reports no paper sources on this machine.");

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = xps, PaperSource = first.ToUpperInvariant() }, out PrinterSettings? settings);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.Equal(first, settings.DefaultPageSettings.PaperSource.SourceName);
    }

    /// <summary>
    /// Settings creation checks the queue before handing out settings: a paused printer is refused with
    /// the paused message, so the job never starts and nothing claims success.
    /// </summary>
    [Fact]
    public void Paused_printer_is_PrinterUnavailable_before_any_settings_are_returned()
    {
        RequirePdfPrinter();
        var probe = FakeStatusProbe.Reading(0x00000001u);

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter }, SystemPrinterCatalog.Instance, probe, out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, status);
        Assert.Null(settings);
        Assert.Equal(Messages.PrinterPaused(PdfPrinter), message);
        Assert.Equal([PdfPrinter], probe.Asked);
    }

    /// <summary>A status probe that fails does not stop settings from being created.</summary>
    [Fact]
    public void Unreadable_printer_status_does_not_block_settings()
    {
        RequirePdfPrinter();
        var probe = new FakeStatusProbe(PrinterStatusReadOutcome.Read) { Throws = new InvalidOperationException("Status unavailable.") };

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter }, SystemPrinterCatalog.Instance, probe, out PrinterSettings? settings, out string message);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.NotNull(settings);
        Assert.Equal(Messages.Ok(), message);
    }

    /// <summary>The probe is asked about the installed spelling of the name, not the caller's.</summary>
    [Fact]
    public void Status_probe_is_asked_about_the_installed_name()
    {
        RequirePdfPrinter();
        var probe = FakeStatusProbe.Reading(0);

        PrintStatus status = PrinterLocator.TryCreateSettings(new PrintOptions { PrinterName = PdfPrinter.ToUpperInvariant() }, SystemPrinterCatalog.Instance, probe, out _, out _);

        Assert.Equal(PrintStatus.Ok, status);
        Assert.Equal([PdfPrinter], probe.Asked);
    }
}
