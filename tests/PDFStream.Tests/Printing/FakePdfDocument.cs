using System.Drawing;
using System.Drawing.Imaging;
using PDFStream.Engine;

namespace PDFStream.Tests.Printing;

/// <summary>The two ways a page can be put on paper.</summary>
internal enum RenderKind
{
    /// <summary>Drawn straight onto the printer's device context.</summary>
    DeviceContext,

    /// <summary>Rendered to a bitmap that is then drawn onto the sheet (the fallback).</summary>
    Bitmap,
}

/// <summary>One render request the printing code made.</summary>
/// <param name="Kind">Device context or bitmap.</param>
/// <param name="PageIndex">Zero-based page index.</param>
/// <param name="DestinationPixels">Device-context target in device pixels; empty for a bitmap.</param>
/// <param name="QuarterTurns">Extra clockwise quarter turns requested.</param>
/// <param name="Dpi">Bitmap resolution; 0 for a device-context call.</param>
internal sealed record RenderCall(RenderKind Kind, int PageIndex, Rectangle DestinationPixels, int QuarterTurns, int Dpi);

/// <summary>
/// A stand-in <see cref="IPdfDocument"/> for printing tests. It records every render request and can
/// refuse or fail them on demand. Built from page sizes, it "renders" a page as solid black (so the
/// output is easy to find on a sheet); built around a real document, it passes the calls on to it.
/// </summary>
internal sealed class FakePdfDocument : IPdfDocument
{
    private readonly IPdfDocument? _inner;
    private readonly SizeF[] _pageSizes;

    /// <summary>Creates a fake document with these page sizes, in points.</summary>
    public FakePdfDocument(params SizeF[] pageSizes)
    {
        _pageSizes = pageSizes;
    }

    /// <summary>Wraps a real document: calls go through to it, but are recorded and can be refused.</summary>
    public FakePdfDocument(IPdfDocument inner)
    {
        _inner = inner;
        _pageSizes = [];
    }

    /// <summary>Every render request, in order.</summary>
    public List<RenderCall> Calls { get; } = [];

    /// <summary>
    /// When <see langword="true"/>, every device-context call throws the exception the engine uses for
    /// a device context that refuses the drawing calls, like a printer driver that cannot take them.
    /// </summary>
    public bool RefuseDeviceContext { get; init; }

    /// <summary>Returns the exception a bitmap render of the given page index throws, or <see langword="null"/>.</summary>
    public Func<int, Exception?>? BitmapFailure { get; init; }

    /// <summary>Runs at the start of every render request, with the page index (for example to cancel a job mid-way).</summary>
    public Action<int>? BeforeRender { get; init; }

    /// <summary>US Letter portrait pages.</summary>
    public static FakePdfDocument Letter(int pages) =>
        new(Enumerable.Repeat(new SizeF(612, 792), pages).ToArray());

    /// <inheritdoc/>
    public int PageCount => _inner?.PageCount ?? _pageSizes.Length;

    /// <inheritdoc/>
    public SizeF GetPageSizePoints(int pageIndex) => _inner?.GetPageSizePoints(pageIndex) ?? _pageSizes[pageIndex];

    /// <inheritdoc/>
    public void RenderToDeviceContext(int pageIndex, IntPtr hdc, Rectangle destinationPixels, bool forPrinting, int quarterTurns)
    {
        Calls.Add(new RenderCall(RenderKind.DeviceContext, pageIndex, destinationPixels, quarterTurns, 0));
        BeforeRender?.Invoke(pageIndex);
        if (RefuseDeviceContext)
        {
            throw new InvalidOperationException("The device context refused the drawing calls (test).");
        }

        if (_inner is not null)
        {
            _inner.RenderToDeviceContext(pageIndex, hdc, destinationPixels, forPrinting, quarterTurns);
            return;
        }

        // A Graphics made from the device context works in device pixels, the same units as the rectangle.
        using Graphics graphics = Graphics.FromHdc(hdc);
        graphics.FillRectangle(Brushes.Black, destinationPixels);
    }

    /// <inheritdoc/>
    public Bitmap RenderToBitmap(int pageIndex, int dpi, int quarterTurns)
    {
        Calls.Add(new RenderCall(RenderKind.Bitmap, pageIndex, Rectangle.Empty, quarterTurns, dpi));
        BeforeRender?.Invoke(pageIndex);
        if (BitmapFailure?.Invoke(pageIndex) is Exception failure)
        {
            throw failure;
        }

        if (_inner is not null)
        {
            return _inner.RenderToBitmap(pageIndex, dpi, quarterTurns);
        }

        SizeF size = GetPageSizePoints(pageIndex);
        bool turned = quarterTurns % 2 == 1;
        int width = Math.Max(1, (int)Math.Round((turned ? size.Height : size.Width) * dpi / 72.0));
        int height = Math.Max(1, (int)Math.Round((turned ? size.Width : size.Height) * dpi / 72.0));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(dpi, dpi);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
        }

        return bitmap;
    }

    /// <summary>Disposes the wrapped document, if any.</summary>
    public void Dispose() => _inner?.Dispose();
}
