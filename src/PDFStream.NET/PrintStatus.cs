namespace PDFStream.NET;

/// <summary>
/// The outcome of a print call. Every failure reason has its own value so a caller can react in
/// code; <see cref="PrintResult.Message"/> carries the same reason in plain language.
/// </summary>
public enum PrintStatus
{
    /// <summary>The pages were rendered and sent to the printer.</summary>
    Ok,

    /// <summary>The PDF file does not exist or could not be read.</summary>
    FileNotFound,

    /// <summary>The data is not a PDF, or the PDF is damaged and cannot be opened.</summary>
    InvalidPdf,

    /// <summary>The PDF is protected and no password was supplied.</summary>
    PasswordRequired,

    /// <summary>The PDF is protected and the supplied password is wrong.</summary>
    PasswordInvalid,

    /// <summary>No printer with the given name is installed.</summary>
    PrinterNotFound,

    /// <summary>The printer exists but could not be opened, or the requested settings are not valid for it.</summary>
    PrinterUnavailable,

    /// <summary>The page range text is malformed, for example "5-2", "0" or "abc".</summary>
    InvalidPageRange,

    /// <summary>The page range is well formed but names a page the PDF does not have.</summary>
    PageNotFound,

    /// <summary>The print options are not usable, for example a missing printer name or fewer than one copy.</summary>
    InvalidOptions,

    /// <summary>Printing was cancelled before it finished; some pages may already have been sent.</summary>
    Cancelled,

    /// <summary>Something unexpected went wrong that none of the other values describes.</summary>
    UnknownError,
}
