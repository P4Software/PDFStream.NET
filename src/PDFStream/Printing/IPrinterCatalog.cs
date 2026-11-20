namespace PDFStream.Printing;

/// <summary>
/// The list of printers Windows knows about. It exists as a seam: the real list comes from the
/// print system, but tests hand in a fake list so printer-name matching (case, spaces, non-ASCII
/// letters, network names such as <c>\\server\printer</c>) and the "no such printer" path can be
/// checked without any printer or print spooler.
/// </summary>
internal interface IPrinterCatalog
{
    /// <summary>
    /// Returns the names of the installed printers exactly as Windows lists them. May throw when
    /// the print system is not available (for example the Print Spooler service is stopped); the
    /// caller turns that into a status.
    /// </summary>
    IReadOnlyList<string> GetInstalledPrinterNames();
}
