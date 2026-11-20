using System.Runtime.InteropServices;

namespace PDFStream.Engine;

/// <summary>
/// P/Invoke declarations for the handful of PDFium functions PDFStream uses, written from the
/// public PDFium header <c>fpdfview.h</c> (BSD-3-Clause) of the bundled build.
/// </summary>
/// <remarks>
/// <para>
/// Never call these directly from outside the engine: PDFium is not thread-safe, and every call
/// must be made while holding <see cref="PdfiumLibrary.Sync"/>.
/// </para>
/// <para>
/// Calling convention: <c>FPDF_CALLCONV</c> only becomes <c>__stdcall</c> when PDFium is built
/// with <c>FPDFSDK_EXPORTS</c>, which the bundled build is not. Disassembly of the win-x86
/// <c>pdfium.dll</c> confirms plain <c>ret</c> (caller clean-up) and undecorated export names,
/// i.e. cdecl. Cdecl is therefore stated explicitly: the .NET default on x86 is stdcall, and a
/// mismatch would corrupt the stack on 32-bit processes only. On x64 and arm64 there is a single
/// calling convention and the setting is ignored.
/// </para>
/// </remarks>
internal static class PdfiumNative
{
    /// <summary>
    /// Base name of the native library. The .NET host finds <c>runtimes/&lt;rid&gt;/native/pdfium.dll</c>
    /// for the architecture of the running process.
    /// </summary>
    private const string Library = "pdfium";

    /// <summary>FPDF_ERR_PASSWORD: password required, or the password given is wrong.</summary>
    public const int ErrorPassword = 4;

    /// <summary>FPDF_ANNOT: draw annotations (stamps, form field appearances, comments).</summary>
    public const int RenderAnnotations = 0x01;

    /// <summary>FPDF_PRINTING: render for printing (honours the annotations' print flags).</summary>
    public const int RenderForPrinting = 0x800;

    /// <summary>FPDFBitmap_BGRA: 4 bytes per pixel in blue, green, red, alpha order, which is the
    /// memory layout of a GDI+ 32bpp ARGB bitmap on little-endian Windows.</summary>
    public const int BitmapFormatBgra = 4;

    /// <summary>Opaque white in PDFium's 0xAARRGGBB colour format.</summary>
    public const uint ColorWhite = 0xFFFFFFFF;

    /// <summary>FS_SIZEF: a width and height in points (named apart from System.Drawing.SizeF).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FsSizeF
    {
        /// <summary>Width in points.</summary>
        public float Width;

        /// <summary>Height in points.</summary>
        public float Height;
    }

    /// <summary>void FPDF_InitLibrary(): initialises PDFium once per process.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDF_InitLibrary();

    /// <summary>
    /// FPDF_DOCUMENT FPDF_LoadMemDocument64(const void* data_buf, size_t size, FPDF_BYTESTRING password).
    /// PDFium does not copy the buffer: it must stay valid until <see cref="FPDF_CloseDocument"/>.
    /// </summary>
    /// <remarks>
    /// <c>SetLastError</c> is on because on Windows PDFium reports why a load failed through the
    /// Win32 last-error value (<c>FPDF_GetLastError</c> is a direct jump to kernel32
    /// <c>GetLastError</c> in this build). The runtime may overwrite that value between two
    /// P/Invoke calls, so it is captured right after this call and read with
    /// <see cref="Marshal.GetLastPInvokeError"/> instead of a second call into PDFium.
    /// The password is passed as UTF-8, which PDFium accepts and converts to Latin-1 itself for
    /// older encryption revisions that expect it.
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr FPDF_LoadMemDocument64(IntPtr dataBuffer, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    /// <summary>void FPDF_CloseDocument(FPDF_DOCUMENT document).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    /// <summary>int FPDF_GetPageCount(FPDF_DOCUMENT document).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    /// <summary>FPDF_BOOL FPDF_GetPageSizeByIndexF(FPDF_DOCUMENT document, int page_index, FS_SIZEF* size).
    /// Reads the size without loading the whole page.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int pageIndex, out FsSizeF size);

    /// <summary>FPDF_PAGE FPDF_LoadPage(FPDF_DOCUMENT document, int page_index).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    /// <summary>void FPDF_ClosePage(FPDF_PAGE page).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDF_ClosePage(IntPtr page);

    /// <summary>
    /// FPDF_BOOL FPDF_RenderPage(HDC dc, FPDF_PAGE page, int start_x, int start_y, int size_x,
    /// int size_y, int rotate, int flags). Windows only; draws onto a device context.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDF_RenderPage(IntPtr hdc, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    /// <summary>
    /// FPDF_BITMAP FPDFBitmap_CreateEx(int width, int height, int format, void* first_scan, int stride).
    /// With an external buffer PDFium draws into memory the caller owns and never frees it.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    /// <summary>FPDF_BOOL FPDFBitmap_FillRect(FPDF_BITMAP bitmap, int left, int top, int width,
    /// int height, FPDF_DWORD color). FPDF_DWORD is a 32-bit <c>unsigned long</c> on Windows.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    /// <summary>void FPDF_RenderPageBitmap(FPDF_BITMAP bitmap, FPDF_PAGE page, int start_x,
    /// int start_y, int size_x, int size_y, int rotate, int flags).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    /// <summary>void FPDFBitmap_Destroy(FPDF_BITMAP bitmap). Does not free an external buffer.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
}
