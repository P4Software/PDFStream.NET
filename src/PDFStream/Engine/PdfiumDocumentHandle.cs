using System.Runtime.InteropServices;

namespace PDFStream.Engine;

/// <summary>
/// Owns one open PDFium document and the native copy of the PDF bytes it was loaded from, and
/// releases both exactly once: on <see cref="SafeHandle.Dispose()"/>, or by the finalizer if a
/// caller forgot to dispose.
/// </summary>
/// <remarks>
/// PDFium reads the file lazily from the buffer it was given for as long as the document is open,
/// so the buffer must outlive the document. Keeping both in one handle makes that ordering
/// impossible to get wrong: the document is closed first, then the buffer is freed.
/// </remarks>
internal sealed class PdfiumDocumentHandle : SafeHandle
{
    private IntPtr _buffer;

    private PdfiumDocumentHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Copies <paramref name="pdf"/> into native memory and asks PDFium to open it.
    /// The caller must hold <see cref="PdfiumLibrary.Sync"/> and have initialised the library.
    /// </summary>
    /// <param name="pdf">The PDF file; must not be empty.</param>
    /// <param name="password">The password, or <see langword="null"/>.</param>
    /// <param name="error">PDFium's FPDF_ERR_* code when the load fails; 0 on success.</param>
    /// <returns>The open document, or <see langword="null"/> if PDFium could not open it.</returns>
    internal static PdfiumDocumentHandle? Open(byte[] pdf, string? password, out int error)
    {
        // The handle object exists before any native work so that, once PDFium has returned a
        // document, nothing that could throw stands between the document and its owner.
        var owner = new PdfiumDocumentHandle();

        // The bytes are copied rather than pinned: the caller keeps ownership of its array and may
        // reuse it, and pinning a large managed array for the document's lifetime would fragment
        // the garbage-collected heap. Rendering costs far more than this one copy.
        IntPtr buffer = Marshal.AllocHGlobal(pdf.Length);
        IntPtr document;
        try
        {
            Marshal.Copy(pdf, 0, buffer, pdf.Length);
            document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)pdf.Length, password);
            error = document == IntPtr.Zero ? Marshal.GetLastPInvokeError() : 0;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            owner.SetHandleAsInvalid();
            throw;
        }

        if (document == IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);

            // Nothing to release: mark the unused handle as closed so its finalizer has no work.
            owner.SetHandleAsInvalid();
            return null;
        }

        owner._buffer = buffer;
        owner.SetHandle(document);
        return owner;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        // This can run on the finalizer thread, so it takes the same lock as every other PDFium
        // call. It may wait for a render on another thread to finish; that is the price of PDFium
        // not being thread-safe, and far better than a crash.
        lock (PdfiumLibrary.Sync)
        {
            PdfiumNative.FPDF_CloseDocument(handle);
        }

        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        return true;
    }
}
