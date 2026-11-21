using System.Runtime.InteropServices;

namespace PDFStream.net.Printing;

/// <summary>
/// The real printer status, read from the Windows print spooler with OpenPrinter and GetPrinter
/// (PRINTER_INFO_2), and the spooler check (see <see cref="IsSpoolerStopped"/>). It never throws for a printer problem: a queue that cannot be opened is reported
/// as <see cref="PrinterStatusReadOutcome.SpoolerNotRunning"/> or
/// <see cref="PrinterStatusReadOutcome.CannotOpen"/>, and a status that cannot be read from an open
/// queue as <see cref="PrinterStatusReadOutcome.Unreadable"/>.
/// </summary>
/// <remarks>
/// The declarations are written from the documented Win32 API (winspool.drv). They use the
/// platform's default calling convention, which is what winspool exports use on every architecture.
/// </remarks>
internal sealed class WinspoolPrinterStatusProbe : IPrinterStatusProbe
{
    /// <summary>The one shared instance; the class holds no state.</summary>
    internal static readonly WinspoolPrinterStatusProbe Instance = new();

    /// <summary>
    /// RPC_S_SERVER_UNAVAILABLE: how OpenPrinter (and the printer list behind
    /// <c>PrinterSettings.InstalledPrinters</c>) fails when the Print Spooler service is not running, because
    /// the spooler is the RPC server they talk to.
    /// </summary>
    internal const int RpcServerUnavailable = 1722;

    /// <summary>GetPrinter information level for PRINTER_INFO_2, the level that carries Status and Attributes.</summary>
    private const int InfoLevel2 = 2;

    private WinspoolPrinterStatusProbe()
    {
    }

    /// <inheritdoc/>
    public PrinterStatusReadOutcome TryRead(string printerName, out uint status, out uint attributes)
    {
        status = 0;
        attributes = 0;

        if (!OpenPrinter(printerName, out IntPtr printer, IntPtr.Zero))
        {
            return Marshal.GetLastPInvokeError() == RpcServerUnavailable
                ? PrinterStatusReadOutcome.SpoolerNotRunning
                : PrinterStatusReadOutcome.CannotOpen;
        }

        try
        {
            // The first call only asks how large the variable-length PRINTER_INFO_2 block is; it fails
            // with "insufficient buffer" by design, so its result is not checked.
            GetPrinter(printer, InfoLevel2, IntPtr.Zero, 0, out int needed);
            if (needed < Marshal.SizeOf<PrinterInfo2>())
            {
                return PrinterStatusReadOutcome.Unreadable;
            }

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(printer, InfoLevel2, buffer, needed, out _))
                {
                    return PrinterStatusReadOutcome.Unreadable;
                }

                PrinterInfo2 info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                status = info.Status;
                attributes = info.Attributes;
                return PrinterStatusReadOutcome.Read;
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

    /// <inheritdoc/>
    /// <remarks>
    /// Opens the local print server itself (OpenPrinter with no printer name), which needs no printer and never
    /// goes over the network, and closes it again at once. Only RPC_S_SERVER_UNAVAILABLE counts as "stopped":
    /// any other refusal (for example a permission problem) says nothing certain about the service.
    /// </remarks>
    public bool IsSpoolerStopped()
    {
        if (OpenPrinter(null, out IntPtr server, IntPtr.Zero))
        {
            ClosePrinter(server);
            return false;
        }

        return Marshal.GetLastPInvokeError() == RpcServerUnavailable;
    }

    /// <summary>OpenPrinterW. A <see langword="null"/> name opens the local print server instead of a printer.</summary>
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool OpenPrinter(string? printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, int level, IntPtr buffer, int bufferSize, out int needed);

    [DllImport("winspool.drv", ExactSpelling = true, SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    /// <summary>
    /// PRINTER_INFO_2W. The thirteen pointers come first, so the layout differs between 32-bit and
    /// 64-bit processes; declaring them as <see cref="IntPtr"/> with sequential layout makes the
    /// marshaller place Attributes and Status correctly on both. The strings are not needed, so they
    /// stay raw pointers into the native buffer and are never copied.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrinterInfo2
    {
        /// <summary>pServerName.</summary>
        public IntPtr ServerName;

        /// <summary>pPrinterName.</summary>
        public IntPtr PrinterName;

        /// <summary>pShareName.</summary>
        public IntPtr ShareName;

        /// <summary>pPortName.</summary>
        public IntPtr PortName;

        /// <summary>pDriverName.</summary>
        public IntPtr DriverName;

        /// <summary>pComment.</summary>
        public IntPtr Comment;

        /// <summary>pLocation.</summary>
        public IntPtr Location;

        /// <summary>pDevMode.</summary>
        public IntPtr DevMode;

        /// <summary>pSepFile.</summary>
        public IntPtr SepFile;

        /// <summary>pPrintProcessor.</summary>
        public IntPtr PrintProcessor;

        /// <summary>pDatatype.</summary>
        public IntPtr Datatype;

        /// <summary>pParameters.</summary>
        public IntPtr Parameters;

        /// <summary>pSecurityDescriptor.</summary>
        public IntPtr SecurityDescriptor;

        /// <summary>PRINTER_ATTRIBUTE_* bits.</summary>
        public uint Attributes;

        /// <summary>Priority.</summary>
        public uint Priority;

        /// <summary>DefaultPriority.</summary>
        public uint DefaultPriority;

        /// <summary>StartTime.</summary>
        public uint StartTime;

        /// <summary>UntilTime.</summary>
        public uint UntilTime;

        /// <summary>PRINTER_STATUS_* bits.</summary>
        public uint Status;

        /// <summary>cJobs.</summary>
        public uint Jobs;

        /// <summary>AveragePPM.</summary>
        public uint AveragePpm;
    }
}
