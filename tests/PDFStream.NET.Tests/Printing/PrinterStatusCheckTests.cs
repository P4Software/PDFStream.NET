using System.ComponentModel;
using System.Runtime.InteropServices;
using PDFStream.NET.Printing;
using Xunit;

namespace PDFStream.NET.Tests.Printing;

/// <summary>
/// Tests the pre-flight printer status check: a queue that is paused, set to "Use printer offline",
/// not available or being deleted, or that cannot be opened (Print Spooler stopped included), is refused
/// with a message naming the cause. Offline reports, paper, toner and similar problems are not refused,
/// and a status that cannot be read never blocks printing. A fake probe supplies every state; two tests
/// use the real system.
/// </summary>
public sealed class PrinterStatusCheckTests
{
    private const string Printer = "Front Desk Laser";
    private const string PdfPrinter = "Microsoft Print to PDF";

    /// <summary>Each blocking status bit (and the work-offline attribute) is PrinterUnavailable with its own message.</summary>
    [Theory]
    [InlineData(0x00000001u, 0u, "paused")]
    [InlineData(0x00001000u, 0u, "not-available")]
    [InlineData(0x00000004u, 0u, "pending-deletion")]
    [InlineData(0u, 0x00000400u, "work-offline")]
    public void Blocking_queue_state_is_PrinterUnavailable_naming_the_cause(uint status, uint attributes, string cause)
    {
        var probe = FakeStatusProbe.Reading(status, attributes);

        PrintStatus result = PrinterStatusCheck.Check(probe, Printer, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, result);
        Assert.Equal(ExpectedMessage(cause), message);
        Assert.Contains(Printer, message);
        Assert.Equal([Printer], probe.Asked);
    }

    /// <summary>
    /// States a person fixes at the printer (paper, jams, toner, doors, intervention), offline reports,
    /// and busy or informational states do not block: the job queues and prints once the printer is ready.
    /// </summary>
    [Theory]

    // Offline and server offline do not block. Network printers often report OFFLINE through SNMP
    // status while they print perfectly well, and a printer that really is switched off simply queues
    // the job until it comes back, so refusing would break working printing.
    [InlineData(0x00000080u)] // offline
    [InlineData(0x02000000u)] // server offline
    [InlineData(0x00000002u)] // error
    [InlineData(0x00000008u)] // paper jam
    [InlineData(0x00000010u)] // paper out
    [InlineData(0x00000020u)] // manual feed
    [InlineData(0x00000040u)] // paper problem
    [InlineData(0x00000100u)] // I/O active
    [InlineData(0x00000200u)] // busy
    [InlineData(0x00000400u)] // printing
    [InlineData(0x00000800u)] // output bin full
    [InlineData(0x00002000u)] // waiting
    [InlineData(0x00004000u)] // processing
    [InlineData(0x00008000u)] // initializing
    [InlineData(0x00010000u)] // warming up
    [InlineData(0x00020000u)] // toner low
    [InlineData(0x00040000u)] // no toner
    [InlineData(0x00080000u)] // page punt
    [InlineData(0x00100000u)] // user intervention
    [InlineData(0x00200000u)] // out of memory
    [InlineData(0x00400000u)] // door open
    [InlineData(0x00800000u)] // server unknown
    [InlineData(0x01000000u)] // power save
    [InlineData(0x04000000u)] // driver update needed
    public void Non_blocking_status_bit_does_not_block(uint status)
    {
        PrintStatus result = PrinterStatusCheck.Check(FakeStatusProbe.Reading(status), Printer, out string message);

        Assert.Equal(PrintStatus.Ok, result);
        Assert.Equal(Messages.Ok(), message);
    }

    /// <summary>All the non-blocking bits at once, and every attribute except work offline, still do not block.</summary>
    [Fact]
    public void All_non_blocking_bits_together_do_not_block()
    {
        const uint blocking = 0x00000001u | 0x00000004u | 0x00001000u;
        const uint workOffline = 0x00000400u;

        PrintStatus result = PrinterStatusCheck.Check(FakeStatusProbe.Reading(~blocking, ~workOffline), Printer, out _);

        Assert.Equal(PrintStatus.Ok, result);
    }

    /// <summary>A blocking bit wins over any non-blocking ones set beside it.</summary>
    [Fact]
    public void Blocking_bit_with_paper_out_beside_it_still_blocks()
    {
        PrintStatus result = PrinterStatusCheck.Check(FakeStatusProbe.Reading(0x00000001u | 0x00000010u), Printer, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, result);
        Assert.Equal(Messages.PrinterPaused(Printer), message);
    }

    /// <summary>A probe that throws never blocks printing: an unreadable status is treated as fine.</summary>
    [Theory]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(DllNotFoundException))]
    public void Probe_that_throws_does_not_block(Type failure)
    {
        var probe = new FakeStatusProbe(PrinterStatusReadOutcome.Read, status: 0x00000001u) { Throws = (Exception)Activator.CreateInstance(failure)! };

        PrintStatus result = PrinterStatusCheck.Check(probe, Printer, out string message);

        Assert.Equal(PrintStatus.Ok, result);
        Assert.Equal(Messages.Ok(), message);
    }

    /// <summary>A queue that opened but whose status could not be read never blocks printing, whatever values came with it.</summary>
    [Fact]
    public void Unreadable_status_does_not_block()
    {
        PrintStatus result = PrinterStatusCheck.Check(new FakeStatusProbe(PrinterStatusReadOutcome.Unreadable, status: 0x00000001u), Printer, out _);

        Assert.Equal(PrintStatus.Ok, result);
    }

    /// <summary>A stopped Print Spooler is PrinterUnavailable with a message that names the Print Spooler service.</summary>
    [Fact]
    public void Stopped_print_spooler_is_PrinterUnavailable_naming_the_spooler()
    {
        PrintStatus result = PrinterStatusCheck.Check(new FakeStatusProbe(PrinterStatusReadOutcome.SpoolerNotRunning), Printer, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, result);
        Assert.Equal(Messages.SpoolerNotRunning(), message);
        Assert.Contains("Print Spooler", message);
    }

    /// <summary>
    /// A printer queue that cannot be opened is PrinterUnavailable. The check only runs for a printer
    /// found in the installed list, so a failed open means the installed queue itself is unusable.
    /// </summary>
    [Fact]
    public void Printer_queue_that_cannot_be_opened_is_PrinterUnavailable()
    {
        PrintStatus result = PrinterStatusCheck.Check(new FakeStatusProbe(PrinterStatusReadOutcome.CannotOpen), Printer, out string message);

        Assert.Equal(PrintStatus.PrinterUnavailable, result);
        Assert.Equal(Messages.PrinterCannotBeOpened(Printer), message);
        Assert.Contains(Printer, message);
        Assert.Contains("Print Spooler", message);
    }

    /// <summary>The real probe reads Microsoft Print to PDF, which is a ready queue: not blocked.</summary>
    [Fact]
    public void Real_probe_reads_Microsoft_Print_to_PDF_as_not_blocked()
    {
        bool installed = SystemPrinterCatalog.Instance.GetInstalledPrinterNames().Contains(PdfPrinter, StringComparer.OrdinalIgnoreCase);
        Assert.SkipUnless(installed, $"The '{PdfPrinter}' printer is not installed on this machine, so the real status probe cannot be checked.");

        PrinterStatusReadOutcome outcome = WinspoolPrinterStatusProbe.Instance.TryRead(PdfPrinter, out uint status, out uint attributes);
        PrintStatus result = PrinterStatusCheck.Check(WinspoolPrinterStatusProbe.Instance, PdfPrinter, out string message);

        Assert.Equal(PrinterStatusReadOutcome.Read, outcome);
        Assert.Equal(PrintStatus.Ok, result);
        Assert.Equal(Messages.Ok(), message);

        // A local queue always carries the LOCAL attribute (0x40), which proves the attributes field
        // was read from the right place in the structure, on 32-bit and 64-bit alike.
        Assert.True((attributes & 0x40u) != 0, $"Expected the LOCAL attribute, got status 0x{status:X8}, attributes 0x{attributes:X8}.");
    }

    /// <summary>The real probe on a printer that does not exist reports that it cannot be opened, instead of throwing.</summary>
    [Fact]
    public void Real_probe_on_an_unknown_printer_reports_it_cannot_be_opened()
    {
        PrinterStatusReadOutcome outcome = WinspoolPrinterStatusProbe.Instance.TryRead("No such printer " + Guid.NewGuid().ToString("N"), out _, out _);

        Assert.Equal(PrinterStatusReadOutcome.CannotOpen, outcome);
    }

    /// <summary>
    /// PRINTER_INFO_2 has 13 pointers before its DWORDs, so its layout differs between 32-bit and
    /// 64-bit processes; the managed copy must match the native one in the running process.
    /// </summary>
    [Fact]
    public void Printer_info_2_layout_matches_the_native_structure_for_this_process()
    {
        int pointers = 13 * IntPtr.Size;

        Assert.Equal(pointers + (8 * sizeof(uint)), Marshal.SizeOf<WinspoolPrinterStatusProbe.PrinterInfo2>());
        Assert.Equal(pointers, (int)Marshal.OffsetOf<WinspoolPrinterStatusProbe.PrinterInfo2>(nameof(WinspoolPrinterStatusProbe.PrinterInfo2.Attributes)));
        Assert.Equal(pointers + (5 * sizeof(uint)), (int)Marshal.OffsetOf<WinspoolPrinterStatusProbe.PrinterInfo2>(nameof(WinspoolPrinterStatusProbe.PrinterInfo2.Status)));
    }

    private static string ExpectedMessage(string cause) => cause switch
    {
        "paused" => Messages.PrinterPaused(Printer),
        "not-available" => Messages.PrinterNotAvailable(Printer),
        "pending-deletion" => Messages.PrinterPendingDeletion(Printer),
        "work-offline" => Messages.PrinterWorkOffline(Printer),
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };
}
