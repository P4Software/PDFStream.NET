using PDFStream.net.Engine;

namespace PDFStream.net;

/// <summary>
/// Turns the three ways a caller can hand over a PDF (a file path, a stream or a byte array)
/// into an open <see cref="IPdfDocument"/>, and every way that can go wrong into a
/// <see cref="PrintStatus"/>. Nothing in here throws for bad input: the public print methods
/// promise a result with a message instead of an exception. The one thing that does come out as an
/// exception is a failure of the engine itself (for example pdfium.dll missing for this processor):
/// it is not about the caller's file, and the facade turns it into an unknown-error result that
/// carries the exception's message, which the person fixing the installation needs to see.
/// </summary>
internal static class PdfSource
{
    /// <summary>Loads a PDF from a file.</summary>
    /// <param name="engine">The engine that opens the PDF.</param>
    /// <param name="path">Path of the PDF file.</param>
    /// <param name="password">Password for a protected PDF; <see langword="null"/> or empty for none.</param>
    /// <param name="doc">The open document when the result is <see cref="PrintStatus.Ok"/>, otherwise
    /// <see langword="null"/>. The caller owns it and must dispose it.</param>
    /// <returns><see cref="PrintStatus.Ok"/>, or the reason the PDF could not be loaded.</returns>
    internal static PrintStatus Load(IPdfEngine engine, string path, string? password, out IPdfDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return PrintStatus.FileNotFound;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (IsFileAccessProblem(ex))
        {
            // Missing file, missing folder, a folder instead of a file, no permission, a sharing
            // violation, a path that is too long or malformed: for the person using the program
            // these are all "the file cannot be found or read", which is what the message says.
            return PrintStatus.FileNotFound;
        }

        return Load(engine, bytes, password, out doc);
    }

    /// <summary>
    /// Loads a PDF from a stream. The stream is read from its current position to its end and is
    /// left open: it belongs to the caller.
    /// </summary>
    /// <param name="engine">The engine that opens the PDF.</param>
    /// <param name="input">A readable stream. It does not need to support seeking.</param>
    /// <param name="password">Password for a protected PDF; <see langword="null"/> or empty for none.</param>
    /// <param name="doc">The open document when the result is <see cref="PrintStatus.Ok"/>, otherwise
    /// <see langword="null"/>. The caller owns it and must dispose it.</param>
    /// <returns><see cref="PrintStatus.Ok"/>, or the reason the PDF could not be loaded.</returns>
    internal static PrintStatus Load(IPdfEngine engine, Stream input, string? password, out IPdfDocument? doc)
    {
        doc = null;
        if (input is null)
        {
            return PrintStatus.InvalidPdf;
        }

        byte[] bytes;
        try
        {
            // Copy to the end instead of trusting Length: network and pipe streams cannot report
            // one, and a stream that is not at the start must give only what is left.
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (IsStreamProblem(ex))
        {
            // The data never arrived in full (closed, broken or unreadable stream), so what the
            // caller handed over is not a complete PDF.
            return PrintStatus.InvalidPdf;
        }

        return Load(engine, bytes, password, out doc);
    }

    /// <summary>Loads a PDF from memory.</summary>
    /// <param name="engine">The engine that opens the PDF.</param>
    /// <param name="input">The complete PDF file.</param>
    /// <param name="password">Password for a protected PDF; <see langword="null"/> or empty for none.</param>
    /// <param name="doc">The open document when the result is <see cref="PrintStatus.Ok"/>, otherwise
    /// <see langword="null"/>. The caller owns it and must dispose it.</param>
    /// <returns><see cref="PrintStatus.Ok"/>, or the reason the PDF could not be loaded.</returns>
    internal static PrintStatus Load(IPdfEngine engine, byte[] input, string? password, out IPdfDocument? doc)
    {
        doc = null;
        if (input is null || input.Length == 0)
        {
            return PrintStatus.InvalidPdf;
        }

        IPdfDocument? opened = null;
        try
        {
            PdfLoadStatus loaded = engine.TryLoad(input, password, out opened);
            switch (loaded)
            {
                case PdfLoadStatus.Ok when opened is not null:
                    if (opened.PageCount < 1)
                    {
                        // A valid PDF with no pages has nothing to print. Reporting it here keeps
                        // every later step free to assume a document has at least one page.
                        return PrintStatus.InvalidPdf;
                    }

                    doc = opened;
                    opened = null;
                    return PrintStatus.Ok;

                case PdfLoadStatus.PasswordRequired:
                    return PrintStatus.PasswordRequired;

                case PdfLoadStatus.PasswordInvalid:
                    return PrintStatus.PasswordInvalid;

                default:
                    // Invalid, and an engine that claims success without a document.
                    return PrintStatus.InvalidPdf;
            }
        }
        finally
        {
            // Anything opened but not handed to the caller (zero pages, or an exception after
            // the open) is released here so native memory is never leaked. An engine failure is
            // deliberately not caught here: the facade reports it with the exception's message,
            // which a bare status could not carry.
            opened?.Dispose();
        }
    }

    /// <summary>True for the exceptions reading a file can raise for reasons outside the caller's code.</summary>
    private static bool IsFileAccessProblem(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException;

    /// <summary>True for the exceptions reading a stream can raise when the stream is broken or unusable.</summary>
    private static bool IsStreamProblem(Exception ex) =>
        ex is IOException or NotSupportedException or ObjectDisposedException or UnauthorizedAccessException;
}
