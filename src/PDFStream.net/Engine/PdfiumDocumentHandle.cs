using System.Runtime.InteropServices;

namespace PDFStream.net.Engine;

/// <summary>
/// Owns one open PDFium document, the native copy of the PDF bytes it was loaded from and, once a page
/// with form fields has been printed, the document's form-fill environment; and releases all of them
/// exactly once: on <see cref="SafeHandle.Dispose()"/>, or by the finalizer if a caller forgot to dispose.
/// </summary>
/// <remarks>
/// PDFium reads the file lazily from the buffer it was given for as long as the document is open,
/// so the buffer must outlive the document; the form-fill environment refers to the document, so it
/// must be closed before the document; and PDFium keeps a pointer to the environment's
/// FPDF_FORMFILLINFO block, so that block must outlive the environment. Keeping all of them in one
/// handle makes that ordering impossible to get wrong: environment, document, then the two blocks.
/// </remarks>
internal sealed class PdfiumDocumentHandle : SafeHandle
{
    private IntPtr _buffer;

    // The form-fill environment and the zeroed FPDF_FORMFILLINFO block it was created with. Both stay
    // IntPtr.Zero until GetOrCreateFormFillEnvironment is first called, which only happens for a document
    // that has a page with form fields. Read and written only while holding PdfiumLibrary.Sync.
    private IntPtr _formEnvironment;
    private IntPtr _formFillInfo;
    private bool _formEnvironmentAttempted;

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

    /// <summary>
    /// Returns the document's form-fill environment, creating it the first time it is asked for. PDFium's
    /// form-fill module builds the appearance of fields (and of other annotations) that have none stored,
    /// which flattening needs; see <see cref="PdfiumDocument"/>.
    /// </summary>
    /// <remarks>
    /// The caller must hold <see cref="PdfiumLibrary.Sync"/> and a reference on this handle (so the document
    /// cannot be closed meanwhile). The environment is created with every host callback left empty and no
    /// JavaScript platform: PDFium checks each callback for null before calling it, so the module never
    /// calls back into the program, never runs a script and never performs an action such as opening a link.
    /// Creation is attempted once; if PDFium refuses, the answer stays <see cref="IntPtr.Zero"/>.
    /// </remarks>
    /// <returns>The environment, or <see cref="IntPtr.Zero"/> when PDFium could not create one.</returns>
    internal IntPtr GetOrCreateFormFillEnvironment()
    {
        if (_formEnvironmentAttempted)
        {
            return _formEnvironment;
        }

        _formEnvironmentAttempted = true;

        int size = PdfiumNative.FormFillInfoSlots * IntPtr.Size;
        IntPtr info = Marshal.AllocHGlobal(size);
        IntPtr environment;
        try
        {
            // Every byte zero (no callbacks, no JavaScript platform), then the version in the first member.
            Marshal.Copy(new byte[size], 0, info, size);
            Marshal.WriteInt32(info, 0, PdfiumNative.FormFillInfoVersion);
            environment = PdfiumNative.FPDFDOC_InitFormFillEnvironment(handle, info);
        }
        catch
        {
            Marshal.FreeHGlobal(info);
            throw;
        }

        if (environment == IntPtr.Zero)
        {
            Marshal.FreeHGlobal(info);
            return IntPtr.Zero;
        }

        // From here on ReleaseHandle owns both and frees them in the right order.
        _formFillInfo = info;
        _formEnvironment = environment;
        return environment;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        // This can run on the finalizer thread, so it takes the same lock as every other PDFium
        // call. It may wait for a render on another thread to finish; that is the price of PDFium
        // not being thread-safe, and far better than a crash.
        lock (PdfiumLibrary.Sync)
        {
            // The form-fill environment refers to the document, so it is closed first.
            if (_formEnvironment != IntPtr.Zero)
            {
                PdfiumNative.FPDFDOC_ExitFormFillEnvironment(_formEnvironment);
                _formEnvironment = IntPtr.Zero;
            }

            PdfiumNative.FPDF_CloseDocument(handle);
        }

        // PDFium kept a pointer to the FPDF_FORMFILLINFO block until the environment was closed, and read
        // the PDF from the buffer until the document was closed; only now may both go.
        if (_formFillInfo != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_formFillInfo);
            _formFillInfo = IntPtr.Zero;
        }

        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        return true;
    }
}
