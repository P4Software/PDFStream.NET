namespace PDFStream.NET.Engine;

/// <summary>
/// Opens PDFs. This seam keeps the PDF binding (PDFium today) out of the public API and lets
/// tests replace the engine with a fake.
/// </summary>
internal interface IPdfEngine
{
    /// <summary>
    /// Tries to open a PDF held in memory.
    /// </summary>
    /// <param name="pdf">The complete PDF file. The engine keeps its own copy, so the caller may
    /// reuse or change the array as soon as this method returns.</param>
    /// <param name="password">The password to open the PDF with. <see langword="null"/> and an
    /// empty string both mean "no password".</param>
    /// <param name="document">The opened document when the result is <see cref="PdfLoadStatus.Ok"/>;
    /// otherwise <see langword="null"/>. The caller owns it and must dispose it.</param>
    /// <returns>Whether the PDF opened and, if not, why.</returns>
    PdfLoadStatus TryLoad(byte[] pdf, string? password, out IPdfDocument? document);
}
