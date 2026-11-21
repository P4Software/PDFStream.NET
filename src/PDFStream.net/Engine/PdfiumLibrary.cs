using System.Diagnostics;

namespace PDFStream.net.Engine;

/// <summary>
/// Process-wide PDFium state: the single lock every native call is made under, and one-time
/// library initialisation.
/// </summary>
/// <remarks>
/// PDFium is not thread-safe: two threads inside it at once, even on different documents, can
/// corrupt its shared caches and crash the process. So there is exactly one lock for the whole
/// process, and documents serialise all their work through it. Callers on several threads are
/// safe; they simply take turns.
/// </remarks>
internal static class PdfiumLibrary
{
    /// <summary>
    /// The lock that must be held around every call into PDFium, including initialisation,
    /// page open and close, rendering and closing a document (also from a finalizer).
    /// </summary>
    internal static readonly object Sync = new();

    // Written and read only while holding Sync, so it needs no volatile or interlocked access.
    private static bool s_initialized;

    /// <summary>
    /// Initialises PDFium the first time it is needed. The caller must already hold <see cref="Sync"/>.
    /// </summary>
    /// <remarks>
    /// PDFium is deliberately never shut down (<c>FPDF_DestroyLibrary</c>). Documents can still be
    /// closed late by the finalizer thread, and closing a document after the library is destroyed
    /// would crash; the operating system reclaims everything when the process exits.
    /// </remarks>
    /// <exception cref="DllNotFoundException">pdfium.dll for this processor architecture is missing.</exception>
    internal static void EnsureInitialized()
    {
        Debug.Assert(Monitor.IsEntered(Sync), "PDFium must only be initialised while holding PdfiumLibrary.Sync.");

        if (s_initialized)
        {
            return;
        }

        PdfiumNative.FPDF_InitLibrary();
        s_initialized = true;
    }
}
