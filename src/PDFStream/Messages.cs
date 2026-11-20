namespace PDFStream;

/// <summary>
/// The plain-language text for every <see cref="PrintStatus"/>. Each message says what happened and,
/// for a failure, ends with what to do next, so it can be shown to an end user as it is.
/// </summary>
internal static class Messages
{
    /// <summary>Returns the standard message for a status.</summary>
    /// <param name="status">The status to describe.</param>
    internal static string For(PrintStatus status) => status switch
    {
        PrintStatus.Ok => Ok(),
        PrintStatus.FileNotFound => FileNotFound(),
        PrintStatus.InvalidPdf => InvalidPdf(),
        PrintStatus.PasswordRequired => PasswordRequired(),
        PrintStatus.PasswordInvalid => PasswordInvalid(),
        PrintStatus.PrinterNotFound => PrinterNotFound(),
        PrintStatus.PrinterUnavailable => PrinterUnavailable(),
        PrintStatus.InvalidPageRange => InvalidPageRange(),
        PrintStatus.PageNotFound => PageNotFound(),
        PrintStatus.InvalidOptions => InvalidOptions(),
        PrintStatus.Cancelled => Cancelled(),
        PrintStatus.UnknownError => UnknownError(),

        // A status added to the enum without a message must fail loudly in the tests rather than
        // reach a user with an empty text.
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No message is defined for this status."),
    };

    /// <summary>The job printed.</summary>
    internal static string Ok() =>
        "The document was printed. All requested pages were sent to the printer.";

    /// <summary>The file is missing or unreadable.</summary>
    internal static string FileNotFound() =>
        "The PDF file could not be found or read. Check that the file path is correct and that this account is allowed to open the file.";

    /// <summary>The bytes are not a usable PDF.</summary>
    internal static string InvalidPdf() =>
        "The file is not a valid PDF, or it is damaged and cannot be opened. Check that the file is a complete PDF and try opening it in a PDF viewer.";

    /// <summary>A protected PDF without a password.</summary>
    internal static string PasswordRequired() =>
        "The PDF is protected with a password and none was given. Enter the password in the print options and try again.";

    /// <summary>A protected PDF with the wrong password.</summary>
    internal static string PasswordInvalid() =>
        "The password given for the PDF is not correct. Check the password and try again.";

    /// <summary>No installed printer has that name.</summary>
    internal static string PrinterNotFound() =>
        "No printer with that name is installed on this computer. Check the printer name exactly as Windows lists it, and that the printer is visible to the account running this program.";

    /// <summary>The printer cannot be opened or rejects the settings.</summary>
    internal static string PrinterUnavailable() =>
        "The printer could not be opened, or it does not accept the requested settings. Check that the printer is switched on and connected, then try again or choose different print settings.";

    /// <summary>The page range text is malformed.</summary>
    internal static string InvalidPageRange() =>
        "The page range is not valid. Use page numbers starting at 1, separated by commas, and write ranges from the lower to the higher number, for example 1-5, 8, 10-12.";

    /// <summary>The page range names a page the PDF does not have.</summary>
    internal static string PageNotFound() =>
        "The page range includes a page that is not in the PDF. Check the number of pages in the document and choose pages within it.";

    /// <summary>The options themselves are unusable.</summary>
    internal static string InvalidOptions() =>
        "The print options are not valid, for example the printer name is empty or the number of copies is less than 1. Check the print options and try again.";

    /// <summary>
    /// The requested paper source (tray) is not one of the printer's. No existing status text can
    /// name the printer's real trays, which is what lets the caller fix the setting, so this one
    /// takes them as a list.
    /// </summary>
    /// <param name="requested">The paper source name the caller asked for.</param>
    /// <param name="available">The paper source names the printer reports; may be empty.</param>
    internal static string UnknownPaperSource(string requested, IReadOnlyList<string> available) =>
        available.Count == 0
            ? $"The paper source \"{requested}\" is not available because this printer does not report any selectable paper sources. Leave the paper source empty to use the printer's default tray."
            : $"The paper source \"{requested}\" is not available on this printer. The available paper sources are: {string.Join(", ", available)}. Use one of these names exactly, or leave the paper source empty to use the printer's default tray.";

    /// <summary>
    /// The output file cannot be written to: the path is malformed, names a folder, or its folder
    /// does not exist. Reported before printing so no job is started that is bound to fail.
    /// </summary>
    /// <param name="path">The output file path the caller gave.</param>
    internal static string InvalidOutputFile(string path) =>
        $"The output file \"{path}\" cannot be used. Check that the path is a file name (not a folder) and that the folder it is in already exists.";

    /// <summary>The job was stopped.</summary>
    internal static string Cancelled() =>
        "Printing was cancelled before it finished, so some pages may not have been printed. Start the job again if the document is still needed.";

    /// <summary>Anything else.</summary>
    internal static string UnknownError() =>
        "Printing failed for an unexpected reason. Try again, and if it keeps happening contact the person who looks after this program.";
}
