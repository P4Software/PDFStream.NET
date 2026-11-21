namespace PDFStream.NET.Engine;

/// <summary>
/// The outcome of asking an <see cref="IPdfEngine"/> to open a PDF.
/// </summary>
internal enum PdfLoadStatus
{
    /// <summary>The PDF opened and a document was returned.</summary>
    Ok,

    /// <summary>
    /// The bytes are not a PDF the engine can read: empty, not a PDF at all, damaged beyond
    /// repair, or protected by a security scheme the engine does not support.
    /// </summary>
    Invalid,

    /// <summary>The PDF needs a password to open and none was given.</summary>
    PasswordRequired,

    /// <summary>The PDF needs a password to open and the one given is wrong. A PDF that opens without a password (restrictions only) never returns this, whatever password is supplied.</summary>
    PasswordInvalid,
}
