namespace PDFStream.Printing;

/// <summary>
/// The pre-flight check of a printer queue's state, made before any job is started. A queue that is
/// paused, offline, set to work offline, not available, being deleted or behind an offline print server
/// would accept the job and hold it without printing, so the caller would be told "printed" while
/// nothing comes out. Those states are refused with a message naming the cause.
/// </summary>
/// <remarks>
/// <para>
/// Problems a person fixes at the printer (paper out, a jam, toner, an open door, "user intervention")
/// are deliberately not refused: the job waits in the queue and prints as soon as someone fixes it.
/// </para>
/// <para>
/// A status that cannot be read never blocks printing. The check is a courtesy that turns a silent
/// hold into a clear message; it must not stop a working printer because Windows could not answer.
/// </para>
/// </remarks>
internal static class PrinterStatusCheck
{
    /// <summary>PRINTER_STATUS_PAUSED: the queue is paused.</summary>
    internal const uint StatusPaused = 0x00000001;

    /// <summary>PRINTER_STATUS_PENDING_DELETION: the printer is being deleted.</summary>
    internal const uint StatusPendingDeletion = 0x00000004;

    /// <summary>PRINTER_STATUS_OFFLINE: the printer is offline.</summary>
    internal const uint StatusOffline = 0x00000080;

    /// <summary>PRINTER_STATUS_NOT_AVAILABLE: the printer is not available for printing.</summary>
    internal const uint StatusNotAvailable = 0x00001000;

    /// <summary>PRINTER_STATUS_SERVER_OFFLINE: the print server sharing the printer is offline.</summary>
    internal const uint StatusServerOffline = 0x02000000;

    /// <summary>PRINTER_ATTRIBUTE_WORK_OFFLINE: the user chose "Use printer offline".</summary>
    internal const uint AttributeWorkOffline = 0x00000400;

    /// <summary>
    /// Reads the printer's queue state and says whether a job may be started.
    /// </summary>
    /// <param name="probe">Where the state comes from.</param>
    /// <param name="printerName">The printer name exactly as Windows lists it.</param>
    /// <param name="message">The plain-language reason when the printer is refused; otherwise the standard Ok text.</param>
    /// <returns><see cref="PrintStatus.Ok"/>, or <see cref="PrintStatus.PrinterUnavailable"/> for a blocking state or a stopped Print Spooler.</returns>
    internal static PrintStatus Check(IPrinterStatusProbe probe, string printerName, out string message)
    {
        PrinterStatusReadOutcome outcome;
        uint status;
        uint attributes;
        try
        {
            outcome = probe.TryRead(printerName, out status, out attributes);
        }
        catch (Exception)
        {
            // An unreadable status is treated as a ready printer, whatever went wrong reading it.
            message = Messages.Ok();
            return PrintStatus.Ok;
        }

        string? refusal = outcome switch
        {
            PrinterStatusReadOutcome.SpoolerNotRunning => Messages.SpoolerNotRunning(),
            PrinterStatusReadOutcome.Read => DescribeBlockingState(status, attributes, printerName),
            _ => null,
        };

        message = refusal ?? Messages.Ok();
        return refusal is null ? PrintStatus.Ok : PrintStatus.PrinterUnavailable;
    }

    /// <summary>
    /// Returns the message for the first blocking state found, or <see langword="null"/> when none is
    /// set. The order goes from the most permanent cause to the one a person is most likely to have set
    /// on purpose, so the message points at what has to be fixed first.
    /// </summary>
    private static string? DescribeBlockingState(uint status, uint attributes, string printerName)
    {
        if ((status & StatusPendingDeletion) != 0)
        {
            return Messages.PrinterPendingDeletion(printerName);
        }

        if ((status & StatusServerOffline) != 0)
        {
            return Messages.PrintServerOffline(printerName);
        }

        if ((status & StatusOffline) != 0)
        {
            return Messages.PrinterOffline(printerName);
        }

        if ((status & StatusNotAvailable) != 0)
        {
            return Messages.PrinterNotAvailable(printerName);
        }

        if ((attributes & AttributeWorkOffline) != 0)
        {
            return Messages.PrinterWorkOffline(printerName);
        }

        if ((status & StatusPaused) != 0)
        {
            return Messages.PrinterPaused(printerName);
        }

        return null;
    }
}
