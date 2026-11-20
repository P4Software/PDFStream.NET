using System.Drawing.Printing;

namespace PDFStream.Printing;

/// <summary>The real printer list, read from the Windows print system.</summary>
internal sealed class SystemPrinterCatalog : IPrinterCatalog
{
    /// <summary>The one shared instance; the class holds no state.</summary>
    internal static readonly SystemPrinterCatalog Instance = new();

    private SystemPrinterCatalog()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledPrinterNames()
    {
        // Copy into an array: the framework collection is a live view of the spooler and is not
        // meant to be held on to.
        var names = new List<string>(PrinterSettings.InstalledPrinters.Count);
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            names.Add(name);
        }

        return names;
    }
}
