namespace PDFStream.NET;

/// <summary>Whether and how a print job uses both sides of the paper.</summary>
public enum DuplexMode
{
    /// <summary>Leave the setting alone and use whatever the printer is configured to do.</summary>
    PrinterDefault,

    /// <summary>Print on one side only.</summary>
    None,

    /// <summary>Print on both sides and flip along the long edge (like the pages of a book).</summary>
    LongEdge,

    /// <summary>Print on both sides and flip along the short edge (like a notepad).</summary>
    ShortEdge,
}
