namespace PDFStream.NET.Engine;

/// <summary>
/// The PDFium implementation of <see cref="IPdfEngine"/>. Holds no state of its own beyond its settings and
/// is safe to share: all PDFium state is process-wide and guarded by <see cref="PdfiumLibrary.Sync"/>.
/// </summary>
internal sealed class PdfiumEngine : IPdfEngine
{
    private readonly PageFlattener? _flattenPage;
    private readonly PageCopier? _copyPage;

    /// <summary>Creates the engine.</summary>
    /// <param name="flattenPage">How the documents it opens flatten a page with form fields (or its copy);
    /// <see langword="null"/> (the default, and what PDFStream.NET uses) means PDFium's own <c>FPDFPage_Flatten</c>.
    /// Tests pass one that fails, to show that a page that cannot be flattened still prints.</param>
    /// <param name="copyPage">How the documents it opens copy their pages with form fields, in one call, before
    /// flattening them; <see langword="null"/> (the default, and what PDFStream.NET uses) means PDFium's own
    /// <c>FPDF_ImportPagesByIndex</c>. Tests pass one that fails, to show that such pages still print, or one that
    /// counts the calls.</param>
    internal PdfiumEngine(PageFlattener? flattenPage = null, PageCopier? copyPage = null)
    {
        _flattenPage = flattenPage;
        _copyPage = copyPage;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A <see langword="null"/> or empty array returns <see cref="PdfLoadStatus.Invalid"/> without
    /// calling PDFium. Owner-only protected PDFs (restrictions but no open password) open without
    /// a password; their restrictions are not enforced, and a password supplied for such a PDF is
    /// ignored rather than rejected.
    /// </remarks>
    /// <exception cref="DllNotFoundException">pdfium.dll for this processor architecture is missing.</exception>
    public PdfLoadStatus TryLoad(byte[] pdf, string? password, out IPdfDocument? document)
    {
        document = null;
        if (pdf is null || pdf.Length == 0)
        {
            return PdfLoadStatus.Invalid;
        }

        lock (PdfiumLibrary.Sync)
        {
            PdfiumLibrary.EnsureInitialized();

            PdfiumDocumentHandle? handle = PdfiumDocumentHandle.Open(pdf, password, out int error);

            // A PDF with only an owner password opens without any password, but PDFium reports a
            // password error when a password that does not match is supplied. A caller that
            // always passes a password (for example a print agent with one setting for all jobs)
            // must not fail on such a file, so try once more with no password before concluding
            // that the password is wrong.
            if (handle is null && error == PdfiumNative.ErrorPassword && !string.IsNullOrEmpty(password))
            {
                handle = PdfiumDocumentHandle.Open(pdf, null, out error);
            }

            if (handle is null)
            {
                return MapLoadError(error, password);
            }

            try
            {
                document = new PdfiumDocument(handle, _flattenPage, _copyPage);
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            return PdfLoadStatus.Ok;
        }
    }

    /// <summary>Turns PDFium's FPDF_ERR_* load error into a <see cref="PdfLoadStatus"/>.</summary>
    private static PdfLoadStatus MapLoadError(int error, string? password)
    {
        // PDFium uses one code for "password needed" and "password wrong". Which one it was
        // depends only on whether the caller actually supplied a password.
        if (error == PdfiumNative.ErrorPassword)
        {
            return string.IsNullOrEmpty(password) ? PdfLoadStatus.PasswordRequired : PdfLoadStatus.PasswordInvalid;
        }

        // Format, file, unknown and unsupported-security errors all mean the same to a caller:
        // this PDF cannot be opened.
        return PdfLoadStatus.Invalid;
    }
}
