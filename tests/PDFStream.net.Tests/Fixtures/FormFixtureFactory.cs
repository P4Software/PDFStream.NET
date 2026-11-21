using System.Drawing;
using System.Text;

namespace PDFStream.net.Tests.Fixtures;

/// <summary>
/// What goes into a PDF built by <see cref="FormFixtureFactory"/>. The defaults give one page holding three
/// form fields (a filled text field, a ticked checkbox and a field that is not meant to be printed) and
/// nothing else, so any ink on the page can only come from the fields.
/// </summary>
public sealed record FormFixtureOptions
{
    /// <summary>
    /// <see langword="true"/> (the default) puts the three form fields on the page; <see langword="false"/>
    /// leaves them out and also drops the /AcroForm entry, which gives a page without any form.
    /// </summary>
    public bool WithFields { get; init; } = true;

    /// <summary>
    /// <see langword="true"/> (the default) lists the fields in the catalog's /AcroForm dictionary, as a
    /// form-filling program writes them. <see langword="false"/> leaves the widget annotations on the page
    /// but removes /AcroForm, which is what page-extraction tools often produce.
    /// </summary>
    public bool WithAcroForm { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the text field carries only its value and no stored appearance, and the
    /// form says /NeedAppearances true: the viewer (here PDFium's form engine) must build the appearance.
    /// </summary>
    public bool NeedAppearances { get; init; }

    /// <summary>
    /// <see langword="true"/> (the default) gives the text field its stored appearance (a black box).
    /// <see langword="false"/> leaves it with only its value, without the form saying /NeedAppearances.
    /// </summary>
    public bool TextFieldHasStoredAppearance { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the page's MediaBox, CropBox, Resources and Rotate are written on the
    /// parent /Pages node instead of the page, so the page inherits them.
    /// </summary>
    public bool InheritPageAttributes { get; init; }

    /// <summary>When <see langword="true"/>, the page has a CropBox 10 points inside its MediaBox on every side.</summary>
    public bool WithCropBox { get; init; }

    /// <summary>The page's /Rotate value: 0, 90, 180 or 270.</summary>
    public int Rotate { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the page content itself draws a black square (<see cref="FormFixtureFactory.PageMark"/>)
    /// and a line of text in Helvetica (<see cref="FormFixtureFactory.PageText"/>), so a test can check that
    /// the page's own drawing survives whatever is done to its fields.
    /// </summary>
    public bool WithPageContent { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the page also carries two ordinary (non-form) Square annotations marked
    /// for printing: one with a stored appearance (<see cref="FormFixtureFactory.MarkupWithAppearance"/>) and
    /// one without (<see cref="FormFixtureFactory.MarkupWithoutAppearance"/>), which PDFium draws from its
    /// colours.
    /// </summary>
    public bool WithMarkupAnnotations { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the page content also draws three black squares through named entries of the
    /// page's /Resources: a form XObject (<see cref="FormFixtureFactory.ResourceXObject"/>), an image
    /// (<see cref="FormFixtureFactory.ResourceImage"/>) and the one glyph of a Type3 font
    /// (<see cref="FormFixtureFactory.ResourceFontGlyph"/>). With <see cref="InheritPageAttributes"/> those
    /// resources are inherited from the /Pages node like the rest.
    /// </summary>
    /// <remarks>
    /// Each square shows whether its resource was found when the page was drawn: PDFium skips an XObject or an
    /// image it cannot find, and replaces a font it cannot find with Helvetica, whose letter A covers only a
    /// small part of the square. (A missing Helvetica, the font of the page's own text, falls back to Helvetica
    /// and so looks the same, which is why these marks are needed.)
    /// </remarks>
    public bool WithResourceMarks { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the document starts with an extra page without form fields, of a different
    /// size (<see cref="FormFixtureFactory.LeadingPageWidthPoints"/> x
    /// <see cref="FormFixtureFactory.LeadingPageHeightPoints"/>), that draws one black square
    /// (<see cref="FormFixtureFactory.LeadingPageMark"/>). The form page becomes page 2 (index 1). The extra page
    /// carries all its own attributes, so it inherits nothing from the /Pages node.
    /// </summary>
    public bool WithLeadingPlainPage { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the page content also draws a black square (<see cref="FormFixtureFactory.HiddenLayerMark"/>)
    /// inside an optional-content layer that the document's layer settings switch off (/OCProperties /D /OFF),
    /// so a correct rendering leaves that square white. The layer is named through the page's /Resources
    /// (/Properties), inherited with the rest under <see cref="InheritPageAttributes"/>.
    /// </summary>
    public bool WithHiddenLayer { get; init; }

    /// <summary>
    /// The page's MediaBox, in place of the default 400 x 300 points (for pages of extreme sizes and shapes). The
    /// fields and marks keep their positions, so on a small page they may lie outside it; the page still has its
    /// form fields. <see langword="null"/> (the default) keeps 400 x 300.
    /// </summary>
    public PdfRect? MediaBox { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the page's /Annots array also names object 99, which the file does not
    /// contain: a damaged reference, of the kind hand edits and some generators leave behind. PDF readers skip
    /// such an entry; PDFium's page copy drops the whole /Annots array because of it.
    /// </summary>
    public bool WithDanglingAnnotationReference { get; init; }
}

/// <summary>
/// A rectangle in PDF user space, in points, with the origin at the bottom-left corner of the page.
/// </summary>
/// <param name="Left">Left edge.</param>
/// <param name="Bottom">Bottom edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Top">Top edge.</param>
public readonly record struct PdfRect(float Left, float Bottom, float Right, float Top)
{
    /// <summary>Width in points.</summary>
    public float Width => Right - Left;

    /// <summary>Height in points.</summary>
    public float Height => Top - Bottom;

    /// <summary>The rectangle as written in a PDF array.</summary>
    public string ToPdfArray() => $"[{Number(Left)} {Number(Bottom)} {Number(Right)} {Number(Top)}]";

    /// <summary>
    /// A number as PDF syntax allows it: plain decimal digits, never exponent notation (which .NET uses for very
    /// large or small floats and which PDF readers do not accept).
    /// </summary>
    private static string Number(float value) => ((double)value).ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Where the rectangle lands in a rendering of an unrotated, uncropped fixture page at
    /// <paramref name="dpi"/>, with two pixels trimmed off every side (where anti-aliasing softens the
    /// edge), so that a fully inked rectangle counts as fully dark.
    /// </summary>
    /// <param name="dpi">The resolution of the rendering.</param>
    public Rectangle ToInnerPixels(int dpi) => ToInnerPixels(dpi, FormFixtureFactory.PageHeightPoints);

    /// <summary>
    /// As <see cref="ToInnerPixels(int)"/>, for an unrotated, uncropped page <paramref name="pageHeightPoints"/>
    /// tall (the leading plain page is not as tall as the form page).
    /// </summary>
    /// <param name="dpi">The resolution of the rendering.</param>
    /// <param name="pageHeightPoints">The height of the page the rectangle is on, in points.</param>
    public Rectangle ToInnerPixels(int dpi, float pageHeightPoints)
    {
        double scale = dpi / 72.0;
        int left = (int)Math.Round(Left * scale) + 2;
        int top = (int)Math.Round((pageHeightPoints - Top) * scale) + 2;
        int right = (int)Math.Round(Right * scale) - 2;
        int bottom = (int)Math.Round((pageHeightPoints - Bottom) * scale) - 2;
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

/// <summary>
/// Builds small PDFs with interactive form fields by writing the PDF objects by hand. PDFsharp 6.2.4 (the
/// test project's generator) cannot create form fields with appearance streams, and writing the objects
/// directly states exactly what each test relies on: which field has which appearance, which flags, and
/// where everything sits on the page.
/// </summary>
/// <remarks>
/// The page is 400 x 300 points. Every appearance is a solid black rectangle filling its annotation, so a
/// field that is drawn shows up as a fully dark rectangle and one that is not leaves white paper.
/// </remarks>
public static class FormFixtureFactory
{
    /// <summary>Page width in points.</summary>
    public const int PageWidthPoints = 400;

    /// <summary>Page height in points.</summary>
    public const int PageHeightPoints = 300;

    /// <summary>The filled text field (value "HELLO"), printable. Its stored appearance is a black box.</summary>
    public static readonly PdfRect TextField = new(20, 200, 220, 260);

    /// <summary>The ticked checkbox (state /On), printable. Its "On" appearance is a black square.</summary>
    public static readonly PdfRect CheckBox = new(300, 200, 340, 240);

    /// <summary>A filled text field without the Print flag: shown on screen, never printed.</summary>
    public static readonly PdfRect NonPrintingField = new(20, 20, 120, 80);

    /// <summary>A Square annotation with a stored black appearance (only with <see cref="FormFixtureOptions.WithMarkupAnnotations"/>).</summary>
    public static readonly PdfRect MarkupWithAppearance = new(160, 20, 220, 80);

    /// <summary>A Square annotation filled black but without a stored appearance (only with <see cref="FormFixtureOptions.WithMarkupAnnotations"/>).</summary>
    public static readonly PdfRect MarkupWithoutAppearance = new(260, 20, 320, 80);

    /// <summary>The black square the page content draws (only with <see cref="FormFixtureOptions.WithPageContent"/>).</summary>
    public static readonly PdfRect PageMark = new(350, 250, 390, 290);

    /// <summary>Roughly where the page content's line of text sits (only with <see cref="FormFixtureOptions.WithPageContent"/>).</summary>
    public static readonly PdfRect PageText = new(130, 130, 260, 170);

    /// <summary>The crop box used with <see cref="FormFixtureOptions.WithCropBox"/>.</summary>
    public static readonly PdfRect CropBox = new(10, 10, 390, 290);

    /// <summary>The square a form XObject of the page's resources draws (only with <see cref="FormFixtureOptions.WithResourceMarks"/>).</summary>
    public static readonly PdfRect ResourceXObject = new(330, 100, 380, 150);

    /// <summary>The square an image of the page's resources fills (only with <see cref="FormFixtureOptions.WithResourceMarks"/>).</summary>
    public static readonly PdfRect ResourceImage = new(20, 100, 70, 150);

    /// <summary>The square the Type3 font of the page's resources draws as its letter A (only with <see cref="FormFixtureOptions.WithResourceMarks"/>).</summary>
    public static readonly PdfRect ResourceFontGlyph = new(270, 100, 320, 150);

    /// <summary>Width in points of the extra first page (only with <see cref="FormFixtureOptions.WithLeadingPlainPage"/>).</summary>
    public const int LeadingPageWidthPoints = 300;

    /// <summary>Height in points of the extra first page (only with <see cref="FormFixtureOptions.WithLeadingPlainPage"/>).</summary>
    public const int LeadingPageHeightPoints = 200;

    /// <summary>The black square on the extra first page, in that page's own coordinates (only with <see cref="FormFixtureOptions.WithLeadingPlainPage"/>).</summary>
    public static readonly PdfRect LeadingPageMark = new(20, 20, 80, 80);

    /// <summary>The square drawn in the switched-off layer, which must stay white (only with <see cref="FormFixtureOptions.WithHiddenLayer"/>).</summary>
    public static readonly PdfRect HiddenLayerMark = new(240, 200, 290, 240);

    /// <summary>Builds the PDF described by <paramref name="options"/> (the defaults when it is <see langword="null"/>).</summary>
    /// <returns>The complete PDF file as bytes.</returns>
    public static byte[] Create(FormFixtureOptions? options = null)
    {
        FormFixtureOptions o = options ?? new FormFixtureOptions();
        bool acroForm = o.WithFields && o.WithAcroForm;

        // Attributes a page may inherit from its parent. Written on the page or on /Pages, never both.
        string mediaBox = "/MediaBox " + (o.MediaBox ?? new PdfRect(0, 0, PageWidthPoints, PageHeightPoints)).ToPdfArray();
        string cropBox = o.WithCropBox ? " /CropBox " + CropBox.ToPdfArray() : string.Empty;
        string rotate = o.Rotate != 0 ? FormattableString.Invariant($" /Rotate {o.Rotate}") : string.Empty;
        string fonts = "/F1 16 0 R" + (o.WithResourceMarks ? " /F3 19 0 R" : string.Empty);
        string xObjects = o.WithResourceMarks ? " /XObject << /X1 17 0 R /Im1 18 0 R >>" : string.Empty;
        string properties = o.WithHiddenLayer ? " /Properties << /MC0 23 0 R >>" : string.Empty;
        string resources = " /Resources << /Font << " + fonts + " >>" + xObjects + properties + " >>";
        string pageAttributes = mediaBox + cropBox + rotate + resources;

        var annotations = new List<string>();
        if (o.WithFields)
        {
            annotations.AddRange(["6 0 R", "8 0 R", "11 0 R"]);
        }

        if (o.WithMarkupAnnotations)
        {
            annotations.AddRange(["13 0 R", "15 0 R"]);
        }

        if (o.WithDanglingAnnotationReference)
        {
            // Object 99 is never written (the file has 24 objects), so this entry cannot be resolved.
            annotations.Add("99 0 R");
        }

        var content = new StringBuilder();
        if (o.WithPageContent)
        {
            content.Append(FormattableString.Invariant($"0 0 0 rg {PageMark.Left} {PageMark.Bottom} {PageMark.Width} {PageMark.Height} re f\n"));
            content.Append(FormattableString.Invariant($"BT /F1 36 Tf 0 0 0 rg {PageText.Left} {PageText.Bottom + 10} Td (WWWW) Tj ET\n"));
        }

        if (o.WithResourceMarks)
        {
            // The XObject is drawn at its own size, the 2 x 2 image is stretched over its square, and the Type3
            // glyph is 1000 x 1000 glyph units (one em), so at this font size it fills its square exactly.
            content.Append(FormattableString.Invariant($"q 1 0 0 1 {ResourceXObject.Left} {ResourceXObject.Bottom} cm /X1 Do Q\n"));
            content.Append(FormattableString.Invariant($"q {ResourceImage.Width} 0 0 {ResourceImage.Height} {ResourceImage.Left} {ResourceImage.Bottom} cm /Im1 Do Q\n"));
            content.Append(FormattableString.Invariant($"BT /F3 {ResourceFontGlyph.Height} Tf 0 0 0 rg {ResourceFontGlyph.Left} {ResourceFontGlyph.Bottom} Td (A) Tj ET\n"));
        }

        if (o.WithHiddenLayer)
        {
            // Marked content tied to the layer /MC0 names in the page's /Properties; the layer is switched off.
            content.Append(FormattableString.Invariant($"/OC /MC0 BDC 0 0 0 rg {HiddenLayerMark.Left} {HiddenLayerMark.Bottom} {HiddenLayerMark.Width} {HiddenLayerMark.Height} re f EMC\n"));
        }

        // Fixed object numbers keep the cross-references readable; objects a variant does not use stay in the
        // file unreferenced, which is valid PDF and invisible to a reader.
        var objects = new string[24];
        string layers = o.WithHiddenLayer ? " /OCProperties << /OCGs [23 0 R] /D << /OFF [23 0 R] >> >>" : string.Empty;
        objects[1] = "<< /Type /Catalog /Pages 2 0 R" + (acroForm ? " /AcroForm 3 0 R" : string.Empty) + layers + " >>";
        string kids = o.WithLeadingPlainPage ? "/Kids [21 0 R 4 0 R] /Count 2" : "/Kids [4 0 R] /Count 1";
        objects[2] = "<< /Type /Pages " + kids + (o.InheritPageAttributes ? " " + pageAttributes : string.Empty) + " >>";
        objects[3] = "<< /Fields [6 0 R 8 0 R 11 0 R]" + (o.NeedAppearances ? " /NeedAppearances true" : string.Empty)
            + " /DR << /Font << /Helv 16 0 R >> >> /DA (/Helv 0 Tf 0 g) >>";
        objects[4] = "<< /Type /Page /Parent 2 0 R" + (o.InheritPageAttributes ? string.Empty : " " + pageAttributes)
            + " /Contents 5 0 R /Annots [" + string.Join(' ', annotations) + "] >>";
        objects[5] = Stream(string.Empty, content.ToString());

        // /F 4 is the Print flag. The text field's value is HELLO; its stored appearance is a black box.
        objects[6] = "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (HELLO) /DA (/Helv 24 Tf 0 g) /Rect " + TextField.ToPdfArray()
            + " /F 4 /P 4 0 R" + (o.NeedAppearances || !o.TextFieldHasStoredAppearance ? string.Empty : " /AP << /N 7 0 R >>") + " >>";
        objects[7] = BlackBox(TextField);

        // A checkbox in its "On" state: /AS picks the "On" appearance (a black square) out of the two.
        objects[8] = "<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /On /AS /On /Rect " + CheckBox.ToPdfArray()
            + " /F 4 /P 4 0 R /AP << /N << /On 9 0 R /Off 10 0 R >> >> >>";
        objects[9] = BlackBox(CheckBox);
        objects[10] = Stream(FormattableString.Invariant($"/Type /XObject /Subtype /Form /BBox [0 0 {CheckBox.Width} {CheckBox.Height}]"), string.Empty);

        // /F 0: no Print flag, so this field is on screen only and must never reach paper.
        objects[11] = "<< /Type /Annot /Subtype /Widget /FT /Tx /T (internal) /V (X) /Rect " + NonPrintingField.ToPdfArray()
            + " /F 0 /P 4 0 R /AP << /N 12 0 R >> >>";
        objects[12] = BlackBox(NonPrintingField);

        objects[13] = "<< /Type /Annot /Subtype /Square /Rect " + MarkupWithAppearance.ToPdfArray() + " /F 4 /P 4 0 R /AP << /N 14 0 R >> >>";
        objects[14] = BlackBox(MarkupWithAppearance);

        // No /AP: PDFium generates the appearance from the border and interior colours (/C and /IC).
        objects[15] = "<< /Type /Annot /Subtype /Square /Rect " + MarkupWithoutAppearance.ToPdfArray()
            + " /F 4 /C [0 0 0] /IC [0 0 0] /BS << /W 2 >> /P 4 0 R >>";
        objects[16] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

        // The resources behind WithResourceMarks: a black form XObject, a 2 x 2 black greyscale image (written
        // in hexadecimal so the file stays ASCII) and a Type3 font whose letter A is a filled em square.
        objects[17] = BlackBox(ResourceXObject);
        objects[18] = Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode", "00000000>");
        objects[19] = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0]"
            + " /CharProcs << /square 20 0 R >> /Encoding << /Type /Encoding /Differences [65 /square] >>"
            + " /FirstChar 65 /LastChar 65 /Widths [1000] /Resources << >> >>";

        // d1 declares an uncoloured glyph (it is filled with the text colour) 1000 units wide.
        objects[20] = Stream(string.Empty, "1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f");

        // The extra first page states every inheritable attribute itself, so nothing on /Pages reaches it.
        PdfRect leadingBox = new(0, 0, LeadingPageWidthPoints, LeadingPageHeightPoints);
        objects[21] = "<< /Type /Page /Parent 2 0 R /MediaBox " + leadingBox.ToPdfArray() + " /CropBox " + leadingBox.ToPdfArray()
            + " /Rotate 0 /Resources << >> /Contents 22 0 R >>";
        objects[22] = Stream(string.Empty, FormattableString.Invariant($"0 0 0 rg {LeadingPageMark.Left} {LeadingPageMark.Bottom} {LeadingPageMark.Width} {LeadingPageMark.Height} re f"));

        // The optional-content group behind WithHiddenLayer, listed as off in the catalog's layer settings.
        objects[23] = "<< /Type /OCG /Name (Hidden) >>";

        return Assemble(objects);
    }

    /// <summary>A form XObject that fills its whole bounding box with black, sized for <paramref name="rect"/>.</summary>
    private static string BlackBox(PdfRect rect) => Stream(
        FormattableString.Invariant($"/Type /XObject /Subtype /Form /BBox [0 0 {rect.Width} {rect.Height}]"),
        FormattableString.Invariant($"0 0 0 rg 0 0 {rect.Width} {rect.Height} re f"));

    /// <summary>A stream object: its dictionary (with the exact /Length) followed by its data.</summary>
    private static string Stream(string dictionary, string data) =>
        $"<< {dictionary} /Length {Encoding.ASCII.GetByteCount(data)} >>\nstream\n{data}\nendstream";

    /// <summary>
    /// Writes the header, the numbered objects, the cross-reference table and the trailer. Everything is
    /// ASCII, so a character count is a byte offset.
    /// </summary>
    private static byte[] Assemble(string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new int[objects.Length];
        for (int number = 1; number < objects.Length; number++)
        {
            offsets[number] = pdf.Length;
            pdf.Append(FormattableString.Invariant($"{number} 0 obj\n{objects[number]}\nendobj\n"));
        }

        int crossReference = pdf.Length;
        pdf.Append(FormattableString.Invariant($"xref\n0 {objects.Length}\n0000000000 65535 f \n"));
        for (int number = 1; number < objects.Length; number++)
        {
            pdf.Append(FormattableString.Invariant($"{offsets[number]:D10} 00000 n \n"));
        }

        pdf.Append(FormattableString.Invariant($"trailer\n<< /Size {objects.Length} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n"));
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
