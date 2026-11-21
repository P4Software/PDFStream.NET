namespace PDFStream.Printing;

/// <summary>What happened when a printer queue's status was read.</summary>
internal enum PrinterStatusReadOutcome
{
    /// <summary>The status and attributes were read.</summary>
    Read,

    /// <summary>
    /// They could not be read (the queue could not be opened or asked). Printing goes ahead as if
    /// the printer were fine: an unreadable status is never a reason to refuse a job.
    /// </summary>
    Unreadable,

    /// <summary>The Windows Print Spooler service is not running, so no printer can be used at all.</summary>
    SpoolerNotRunning,
}

/// <summary>
/// Reads a printer queue's status as Windows reports it. It exists as a seam: the real reading comes
/// from the print spooler, but tests hand in a fake so every queue state (paused, offline, paper out,
/// and so on) can be checked without pausing or unplugging a real printer.
/// </summary>
internal interface IPrinterStatusProbe
{
    /// <summary>Reads the queue's status bits and attribute bits.</summary>
    /// <param name="printerName">The printer name exactly as Windows lists it.</param>
    /// <param name="status">The PRINTER_STATUS_* bits when the outcome is <see cref="PrinterStatusReadOutcome.Read"/>; otherwise 0.</param>
    /// <param name="attributes">The PRINTER_ATTRIBUTE_* bits when the outcome is <see cref="PrinterStatusReadOutcome.Read"/>; otherwise 0.</param>
    /// <returns>Whether the values were read and, if not, why. May also throw; callers treat that as unreadable.</returns>
    PrinterStatusReadOutcome TryRead(string printerName, out uint status, out uint attributes);
}
