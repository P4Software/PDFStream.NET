using System.Runtime.InteropServices;

namespace PDFStream.net.Engine;

/// <summary>
/// P/Invoke declarations for the handful of PDFium functions PDFStream.net uses, written from the
/// public PDFium headers (<c>fpdfview.h</c>, <c>fpdf_formfill.h</c>, <c>fpdf_flatten.h</c>,
/// <c>fpdf_annot.h</c>, <c>fpdf_transformpage.h</c> and <c>fpdf_ppo.h</c>, BSD-3-Clause) of the bundled build.
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

    /// <summary>
    /// FPDF_ANNOT: draw the page's annotations that have an appearance (stamps, comments, shapes).
    /// It does NOT draw form fields: PDFium's two page-drawing calls (<see cref="FPDF_RenderPage"/> and
    /// <see cref="FPDF_RenderPageBitmap"/>) never draw widget annotations, whatever the flags. Only the
    /// form-fill module's own drawing call (which draws only into a bitmap) or flattening the fields into the
    /// page content does; <see cref="PdfiumDocument"/> flattens them into a copy of the page, or into the page
    /// itself (see its remarks).
    /// </summary>
    public const int RenderAnnotations = 0x01;

    /// <summary>FPDF_PRINTING: render for printing (honours the annotations' print flags).</summary>
    public const int RenderForPrinting = 0x800;

    /// <summary>FPDFBitmap_BGRA: 4 bytes per pixel in blue, green, red, alpha order, which is the
    /// memory layout of a GDI+ 32bpp ARGB bitmap on little-endian Windows.</summary>
    public const int BitmapFormatBgra = 4;

    /// <summary>Opaque white in PDFium's 0xAARRGGBB colour format.</summary>
    public const uint ColorWhite = 0xFFFFFFFF;

    /// <summary>FPDF_ANNOT_WIDGET: the annotation subtype of a form field on a page.</summary>
    public const int AnnotationWidget = 20;

    /// <summary>FLAT_PRINT: flatten the annotations as they print (only those with the Print flag).</summary>
    public const int FlattenForPrint = 1;

    /// <summary>FLATTEN_SUCCESS: annotations were flattened into the page content.</summary>
    public const int FlattenSuccess = 1;

    /// <summary>
    /// The FPDF_FORMFILLINFO version PDFStream.net declares. Version 1 means "stable interfaces only": PDFium
    /// reads the version-2 members only when the version is 2 or more.
    /// </summary>
    public const int FormFillInfoVersion = 1;

    /// <summary>
    /// How many pointer-sized slots PDFStream.net allocates for FPDF_FORMFILLINFO. In the bundled build the
    /// structure is an <c>int</c> version followed by 34 pointer-sized members (the host callbacks, the
    /// JavaScript platform pointer and the version-2 additions), so 64 slots, all zero, leave a wide margin.
    /// </summary>
    public const int FormFillInfoSlots = 64;

    /// <summary>
    /// FS_RECTF: a rectangle in PDF user space, in points. Note the member order (left, top, right, bottom);
    /// for a page box <see cref="Top"/> is greater than <see cref="Bottom"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FsRectF
    {
        /// <summary>Left edge.</summary>
        public float Left;

        /// <summary>Top edge.</summary>
        public float Top;

        /// <summary>Right edge.</summary>
        public float Right;

        /// <summary>Bottom edge.</summary>
        public float Bottom;
    }

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

    /// <summary>
    /// FPDF_FORMHANDLE FPDFDOC_InitFormFillEnvironment(FPDF_DOCUMENT document, FPDF_FORMFILLINFO* formInfo).
    /// Returns NULL on failure. PDFium keeps the <paramref name="formInfo"/> pointer: the block must stay
    /// valid until <see cref="FPDFDOC_ExitFormFillEnvironment"/>, and the environment must be closed before
    /// the document.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr document, IntPtr formInfo);

    /// <summary>void FPDFDOC_ExitFormFillEnvironment(FPDF_FORMHANDLE hHandle). A no-op for NULL.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr formHandle);

    /// <summary>
    /// void FORM_OnAfterLoadPage(FPDF_PAGE page, FPDF_FORMHANDLE hHandle). Gives the form-fill module the
    /// page; it reads the page's annotations and builds the appearances that are missing (or, when the form
    /// says /NeedAppearances, rebuilds the fields' appearances from their values).
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FORM_OnAfterLoadPage(IntPtr page, IntPtr formHandle);

    /// <summary>void FORM_OnBeforeClosePage(FPDF_PAGE page, FPDF_FORMHANDLE hHandle). Must follow
    /// <see cref="FORM_OnAfterLoadPage"/> before the page is closed.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FORM_OnBeforeClosePage(IntPtr page, IntPtr formHandle);

    /// <summary>
    /// int FPDFPage_Flatten(FPDF_PAGE page, int nFlag). Draws the page's annotation appearances into its
    /// content and removes the annotations. Returns FLATTEN_FAIL (0), FLATTEN_SUCCESS (1) or
    /// FLATTEN_NOTHINGTODO (2). The page object passed in keeps its old content; load the page again to
    /// draw the result.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDFPage_Flatten(IntPtr page, int usage);

    /// <summary>int FPDFPage_GetAnnotCount(FPDF_PAGE page).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDFPage_GetAnnotCount(IntPtr page);

    /// <summary>FPDF_ANNOTATION FPDFPage_GetAnnot(FPDF_PAGE page, int index). NULL on failure; close the
    /// result with <see cref="FPDFPage_CloseAnnot"/>.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

    /// <summary>FPDF_ANNOTATION_SUBTYPE FPDFAnnot_GetSubtype(FPDF_ANNOTATION annot) (an <c>int</c>).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDFAnnot_GetSubtype(IntPtr annotation);

    /// <summary>void FPDFPage_CloseAnnot(FPDF_ANNOTATION annot).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDFPage_CloseAnnot(IntPtr annotation);

    /// <summary>
    /// FPDF_BOOL FPDF_GetPageBoundingBox(FPDF_PAGE page, FS_RECTF* rect): the page's visible box, the
    /// intersection of its MediaBox and CropBox, including boxes the page inherits from its parents.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDF_GetPageBoundingBox(IntPtr page, out FsRectF rect);

    /// <summary>
    /// FPDF_BOOL FPDFPage_GetMediaBox(FPDF_PAGE page, float* left, float* bottom, float* right, float* top).
    /// Reads only the page's own dictionary: an inherited MediaBox reports false.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    /// <summary>void FPDFPage_SetMediaBox(FPDF_PAGE page, float left, float bottom, float right, float top).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDFPage_SetMediaBox(IntPtr page, float left, float bottom, float right, float top);

    /// <summary>void FPDFPage_SetCropBox(FPDF_PAGE page, float left, float bottom, float right, float top).</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FPDFPage_SetCropBox(IntPtr page, float left, float bottom, float right, float top);

    /// <summary>
    /// FPDF_BOOL FPDF_ImportPagesByIndex(FPDF_DOCUMENT dest_doc, FPDF_DOCUMENT src_doc, const int* page_indices,
    /// unsigned long length, int index) (<c>fpdf_ppo.h</c>, marked experimental). Inserts copies of the listed
    /// pages of <paramref name="source"/> into <paramref name="destination"/> before page <paramref name="index"/>
    /// (the page count appends them). Returns false if a page index is invalid.
    /// </summary>
    /// <remarks>
    /// PDFStream.net passes the same document as both, to copy a page with form fields to the end of its own
    /// in-memory document. The copy gets the attributes the page inherits from the page tree written onto itself,
    /// and its own copies of the objects the page refers to (checked with the bundled build: a copy of a page that
    /// inherits its MediaBox, Resources and Rotate draws the same pixels as the page, and has a MediaBox of its
    /// own). <c>unsigned long</c> is 32 bits on Windows, for x86 and x64 alike.
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FPDF_ImportPagesByIndex(IntPtr destination, IntPtr source, int[] pageIndices, uint length, int index);
}
