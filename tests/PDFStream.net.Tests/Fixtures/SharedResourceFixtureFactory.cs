using System.Text;

namespace PDFStream.net.Tests.Fixtures;

/// <summary>How the pages of a <see cref="SharedResourceFixtureFactory"/> PDF get their one shared /Resources dictionary.</summary>
public enum SharedResources
{
    /// <summary>The dictionary is written once, inline, on the /Pages node, and every page inherits it.</summary>
    Inherited,

    /// <summary>The dictionary is one indirect object that every page names as its own /Resources.</summary>
    Indirect,
}

/// <summary>
/// Builds PDFs of many pages that all share one /Resources dictionary holding several large images, each page
/// drawing one of them and (optionally) carrying one filled text field. Generators do write PDFs like this, with
/// one dictionary for the whole document. It is the shape that made copying form pages one by one multiply
/// memory: every copy cloned everything the dictionary holds, so the cost grew with the number of form pages
/// times the size of the shared images.
/// </summary>
/// <remarks>
/// Pages are <see cref="PageWidthPoints"/> x <see cref="PageHeightPoints"/> points. The images are
/// <see cref="ImageSide"/> x <see cref="ImageSide"/> 8-bit grey pixels, stored uncompressed (one megabyte each), all
/// black, and each page draws its image over <see cref="ImageMark"/>. A page whose image is missing on paper
/// therefore shows a white square there. The field of page <c>p</c> lies at <see cref="FieldOn"/>(p): four
/// different places, so a test can tell whose field landed on which page.
/// </remarks>
public static class SharedResourceFixtureFactory
{
    /// <summary>Page width in points.</summary>
    public const int PageWidthPoints = 400;

    /// <summary>Page height in points.</summary>
    public const int PageHeightPoints = 300;

    /// <summary>Width and height, in pixels, of every shared image (one byte per pixel, so one megabyte each).</summary>
    public const int ImageSide = 1000;

    /// <summary>The square each page fills with its shared image.</summary>
    public static readonly PdfRect ImageMark = new(20, 20, 120, 120);

    /// <summary>Where the filled text field of page <paramref name="pageIndex"/> lies (one of four places).</summary>
    /// <param name="pageIndex">The zero-based page index.</param>
    public static PdfRect FieldOn(int pageIndex)
    {
        float left = 20 + (90 * (pageIndex % 4));
        return new PdfRect(left, 200, left + 80, 250);
    }

    /// <summary>Builds the PDF.</summary>
    /// <param name="pages">How many pages.</param>
    /// <param name="images">How many one-megabyte images the shared dictionary holds; page <c>p</c> draws image <c>p % images</c>.</param>
    /// <param name="withFields">Whether every page carries a filled, printable text field (and the catalog an /AcroForm).</param>
    /// <param name="resources">How the pages get the shared dictionary.</param>
    /// <param name="sharedContentArray">
    /// When <see langword="true"/>, every page names one and the same indirect /Contents array (holding one content
    /// stream that draws the first image), as a generator that writes identical pages once might. Flattening
    /// appends to a page's content array, so pages that share one must not end up drawing each other's fields.
    /// </param>
    /// <returns>The complete PDF file as bytes.</returns>
    public static byte[] Create(int pages, int images, bool withFields, SharedResources resources, bool sharedContentArray = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(images);

        // Object numbers: 1 catalog, 2 /Pages, 3 /AcroForm, 4 the shared /Resources (used when indirect), then
        // the images, then four objects per page (page, content, widget, widget appearance), then the shared
        // content stream and the shared content array (used with sharedContentArray).
        const int Catalog = 1, PageTree = 2, AcroForm = 3, SharedDictionary = 4;
        int firstImage = 5;
        int firstPage = firstImage + images;
        int sharedContentStream = firstPage + (4 * pages);
        int sharedContents = sharedContentStream + 1;
        int objectCount = sharedContents + 1;
        var bodies = new byte[objectCount][];

        var xObjects = new StringBuilder();
        for (int i = 0; i < images; i++)
        {
            xObjects.Append(FormattableString.Invariant($"/Im{i} {firstImage + i} 0 R "));
        }

        string sharedDictionary = "<< /XObject << " + xObjects + ">> >>";
        var kids = new StringBuilder();
        var fields = new StringBuilder();
        for (int p = 0; p < pages; p++)
        {
            int page = firstPage + (4 * p);
            kids.Append(FormattableString.Invariant($"{page} 0 R "));
            if (withFields)
            {
                fields.Append(FormattableString.Invariant($"{page + 2} 0 R "));
            }
        }

        string inheritedResources = resources == SharedResources.Inherited ? " /Resources " + sharedDictionary : string.Empty;
        bodies[Catalog] = Ascii("<< /Type /Catalog /Pages 2 0 R" + (withFields ? " /AcroForm 3 0 R" : string.Empty) + " >>");
        bodies[PageTree] = Ascii(FormattableString.Invariant($"<< /Type /Pages /Kids [{kids}] /Count {pages} /MediaBox [0 0 {PageWidthPoints} {PageHeightPoints}]{inheritedResources} >>"));
        bodies[AcroForm] = Ascii("<< /Fields [" + fields + "] >>");
        bodies[SharedDictionary] = Ascii(sharedDictionary);

        // All zero bytes: black in DeviceGray. The same buffer serves every image; only the PDF holds copies.
        byte[] imageData = new byte[ImageSide * ImageSide];
        for (int i = 0; i < images; i++)
        {
            bodies[firstImage + i] = Stream(FormattableString.Invariant($"/Type /XObject /Subtype /Image /Width {ImageSide} /Height {ImageSide} /ColorSpace /DeviceGray /BitsPerComponent 8"), imageData);
        }

        for (int p = 0; p < pages; p++)
        {
            int page = firstPage + (4 * p);
            PdfRect field = FieldOn(p);
            string ownResources = resources == SharedResources.Indirect ? " /Resources 4 0 R" : string.Empty;
            string annotations = withFields ? FormattableString.Invariant($" /Annots [{page + 2} 0 R]") : string.Empty;
            int contents = sharedContentArray ? sharedContents : page + 1;
            bodies[page] = Ascii(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /Contents {contents} 0 R{ownResources}{annotations} >>"));
            bodies[page + 1] = Stream(string.Empty, Ascii(FormattableString.Invariant($"q {ImageMark.Width} 0 0 {ImageMark.Height} {ImageMark.Left} {ImageMark.Bottom} cm /Im{p % images} Do Q")));

            // /F 4 is the Print flag; the stored appearance is a black box the size of the field.
            bodies[page + 2] = Ascii(FormattableString.Invariant($"<< /Type /Annot /Subtype /Widget /FT /Tx /T (field{p}) /V (x) /Rect {field.ToPdfArray()} /F 4 /P {page} 0 R /AP << /N {page + 3} 0 R >> >>"));
            bodies[page + 3] = Stream(FormattableString.Invariant($"/Type /XObject /Subtype /Form /BBox [0 0 {field.Width} {field.Height}]"), Ascii(FormattableString.Invariant($"0 0 0 rg 0 0 {field.Width} {field.Height} re f")));
        }

        // Always written (a valid PDF may hold objects nothing refers to); only used with sharedContentArray.
        bodies[sharedContentStream] = Stream(string.Empty, Ascii(FormattableString.Invariant($"q {ImageMark.Width} 0 0 {ImageMark.Height} {ImageMark.Left} {ImageMark.Bottom} cm /Im0 Do Q")));
        bodies[sharedContents] = Ascii(FormattableString.Invariant($"[{sharedContentStream} 0 R]"));
        return Assemble(bodies);
    }

    /// <summary>The bytes of an ASCII string.</summary>
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>A stream object: its dictionary (with the exact /Length) followed by its raw data.</summary>
    private static byte[] Stream(string dictionary, byte[] data)
    {
        using var stream = new MemoryStream();
        stream.Write(Ascii(FormattableString.Invariant($"<< {dictionary} /Length {data.Length} >>\nstream\n")));
        stream.Write(data);
        stream.Write(Ascii("\nendstream"));
        return stream.ToArray();
    }

    /// <summary>Writes the header, the numbered objects, the cross-reference table and the trailer.</summary>
    private static byte[] Assemble(byte[][] bodies)
    {
        using var pdf = new MemoryStream();
        pdf.Write(Ascii("%PDF-1.7\n"));
        var offsets = new long[bodies.Length];
        for (int number = 1; number < bodies.Length; number++)
        {
            offsets[number] = pdf.Position;
            pdf.Write(Ascii(FormattableString.Invariant($"{number} 0 obj\n")));
            pdf.Write(bodies[number]);
            pdf.Write(Ascii("\nendobj\n"));
        }

        long crossReference = pdf.Position;
        pdf.Write(Ascii(FormattableString.Invariant($"xref\n0 {bodies.Length}\n0000000000 65535 f \n")));
        for (int number = 1; number < bodies.Length; number++)
        {
            pdf.Write(Ascii(FormattableString.Invariant($"{offsets[number]:D10} 00000 n \n")));
        }

        pdf.Write(Ascii(FormattableString.Invariant($"trailer\n<< /Size {bodies.Length} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n")));
        return pdf.ToArray();
    }
}
