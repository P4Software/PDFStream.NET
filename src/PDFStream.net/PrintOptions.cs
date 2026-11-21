namespace PDFStream.net;

/// <summary>
/// The settings for one print job. Every property is init-only: build the object once with an
/// object initializer and pass it to the print call. Anything left out keeps the printer's own
/// setting, except where a default is stated.
/// </summary>
public sealed class PrintOptions
{
    /// <summary>
    /// The printer to print to, exactly as Windows lists it, including network names such as
    /// <c>\\server\printer</c>. Required: an empty name makes the print call return
    /// <see cref="PrintStatus.InvalidOptions"/> instead of printing.
    /// </summary>
    /// <remarks>
    /// It defaults to an empty string rather than being a C# "required" member so that a
    /// forgotten name becomes a normal result with a message, not a compile-time wall for callers
    /// who fill the options from a settings file.
    /// </remarks>
    public string PrinterName { get; init; } = string.Empty;

    /// <summary>The name shown for the job in the print queue. <see langword="null"/> lets the library choose one.</summary>
    public string? DocumentName { get; init; }

    /// <summary>The password of a protected PDF. <see langword="null"/> or empty means the PDF has no password.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// The pages to print, counted from 1, for example <c>"1-5, 8, 10-12"</c>. Pages print in the
    /// order written. <see langword="null"/> or empty means every page.
    /// </summary>
    public string? Pages { get; init; }

    /// <summary>How many copies to print. Must be at least 1; the default is 1.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>Single-sided or double-sided printing. The default leaves the printer's own setting.</summary>
    public DuplexMode Duplex { get; init; }

    /// <summary>
    /// Colour or black and white. <see langword="null"/> (the default) keeps the printer's own colour setting,
    /// <see langword="true"/> asks the printer for colour and <see langword="false"/> for black and white.
    /// </summary>
    /// <remarks>
    /// The default changes nothing on purpose: a program that sets no colour gets what the printer is set up to
    /// do, as it would printing from any other application. A printer that cannot print in colour prints in
    /// black and white whatever is asked.
    /// </remarks>
    public bool? Color { get; init; }

    /// <summary>How pages are sized onto the paper. The default fits each page to the paper.</summary>
    public ScaleMode Scale { get; init; } = ScaleMode.FitToPage;

    /// <summary>The paper source (tray) name as the printer reports it. <see langword="null"/> keeps the printer's default tray.</summary>
    public string? PaperSource { get; init; }

    /// <summary>
    /// When set, the job is written to this file instead of being sent to the printer's port. It
    /// is how a "print to file" printer such as Microsoft Print to PDF runs without showing a Save As dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder must already exist; a relative path is taken from the current folder of the process. An
    /// existing file is replaced. An empty or blank text is not the same as leaving the option out: it returns
    /// <see cref="PrintStatus.InvalidOptions"/>.
    /// </para>
    /// <para>
    /// The print system may still be writing the file when the print call returns: Microsoft Print to PDF
    /// finishes it a moment later. A program that opens the file straight afterwards must wait until the file
    /// can be opened on its own (try again every half second or so, for a few seconds) instead of reading it at once.
    /// </para>
    /// </remarks>
    public string? OutputFile { get; init; }
}
