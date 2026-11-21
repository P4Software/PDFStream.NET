using System.Text;
using PDFStream.net.Engine;
using PDFStream.net.Tests.Fixtures;
using Xunit;

namespace PDFStream.net.Tests;

/// <summary>
/// Tests <see cref="PdfSource"/>: turning a file path, a stream or a byte array into an open
/// document, and every way that can fail into a <see cref="PrintStatus"/> instead of an exception.
/// </summary>
public sealed class PdfSourceTests : IDisposable
{
    private const string UserPassword = "open-sesame";

    private readonly PdfiumEngine _engine = new();
    private readonly List<string> _tempFiles = [];

    /// <summary>Deletes the temporary files this test created.</summary>
    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            File.Delete(path);
        }
    }

    /// <summary>A path that does not exist is reported as FileNotFound, without throwing.</summary>
    [Fact]
    public void Missing_file_is_FileNotFound()
    {
        string path = Path.Combine(Path.GetTempPath(), "pdfstream.net-" + Guid.NewGuid().ToString("N") + ".pdf");

        PrintStatus status = PdfSource.Load(_engine, path, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.FileNotFound, status);
        Assert.Null(doc);
    }

    /// <summary>A null, empty or directory path is also FileNotFound: there is no file to read.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_path_is_FileNotFound(string? path)
    {
        PrintStatus status = PdfSource.Load(_engine, path!, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.FileNotFound, status);
        Assert.Null(doc);
    }

    /// <summary>A folder is not a PDF file, so reading it fails as FileNotFound.</summary>
    [Fact]
    public void Directory_path_is_FileNotFound()
    {
        PrintStatus status = PdfSource.Load(_engine, Path.GetTempPath(), null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.FileNotFound, status);
        Assert.Null(doc);
    }

    /// <summary>A real PDF file on disk loads and has its pages.</summary>
    [Fact]
    public void File_path_loads()
    {
        string path = Path.Combine(Path.GetTempPath(), "pdfstream.net-" + Guid.NewGuid().ToString("N") + ".pdf");
        _tempFiles.Add(path);
        File.WriteAllBytes(path, FixtureFactory.CreatePdf(2));

        PrintStatus status = PdfSource.Load(_engine, path, null, out IPdfDocument? doc);

        using (doc)
        {
            Assert.Equal(PrintStatus.Ok, status);
            Assert.NotNull(doc);
            Assert.Equal(2, doc.PageCount);
        }
    }

    /// <summary>A byte array that holds a PDF loads.</summary>
    [Fact]
    public void Bytes_load()
    {
        PrintStatus status = PdfSource.Load(_engine, FixtureFactory.CreatePdf(3), null, out IPdfDocument? doc);

        using (doc)
        {
            Assert.Equal(PrintStatus.Ok, status);
            Assert.NotNull(doc);
            Assert.Equal(3, doc.PageCount);
        }
    }

    /// <summary>A null or empty byte array is InvalidPdf.</summary>
    [Fact]
    public void Null_and_empty_bytes_are_InvalidPdf()
    {
        Assert.Equal(PrintStatus.InvalidPdf, PdfSource.Load(_engine, (byte[])null!, null, out IPdfDocument? first));
        Assert.Null(first);
        Assert.Equal(PrintStatus.InvalidPdf, PdfSource.Load(_engine, Array.Empty<byte>(), null, out IPdfDocument? second));
        Assert.Null(second);
    }

    /// <summary>A stream with no bytes in it is InvalidPdf.</summary>
    [Fact]
    public void Empty_stream_is_InvalidPdf()
    {
        using var stream = new MemoryStream();

        PrintStatus status = PdfSource.Load(_engine, stream, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>A null stream is InvalidPdf rather than an exception.</summary>
    [Fact]
    public void Null_stream_is_InvalidPdf()
    {
        PrintStatus status = PdfSource.Load(_engine, (Stream)null!, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>
    /// A stream that cannot seek (a network or pipe stream) loads. PDFStream.net reads it to the end
    /// and must not depend on Length or Position.
    /// </summary>
    [Fact]
    public void Non_seekable_stream_loads()
    {
        using var stream = new ForwardOnlyStream(FixtureFactory.CreatePdf(2), chunkSize: 100);

        PrintStatus status = PdfSource.Load(_engine, stream, null, out IPdfDocument? doc);

        using (doc)
        {
            Assert.Equal(PrintStatus.Ok, status);
            Assert.NotNull(doc);
            Assert.Equal(2, doc.PageCount);
        }
    }

    /// <summary>The caller's stream stays open: it belongs to the caller.</summary>
    [Fact]
    public void Stream_is_left_open()
    {
        using var stream = new MemoryStream(FixtureFactory.CreatePdf(1));

        PrintStatus status = PdfSource.Load(_engine, stream, null, out IPdfDocument? doc);
        doc?.Dispose();

        Assert.Equal(PrintStatus.Ok, status);
        Assert.True(stream.CanRead);
    }

    /// <summary>A stream that cannot be read at all is InvalidPdf, not an exception.</summary>
    [Fact]
    public void Unreadable_stream_is_InvalidPdf()
    {
        using var stream = new FailingStream();

        PrintStatus status = PdfSource.Load(_engine, stream, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>Text that is not a PDF is InvalidPdf.</summary>
    [Fact]
    public void Garbage_bytes_are_InvalidPdf()
    {
        byte[] garbage = Encoding.ASCII.GetBytes("This is plain text pretending to be a PDF file.");

        PrintStatus status = PdfSource.Load(_engine, garbage, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>A PDF cut off halfway is InvalidPdf and never an exception.</summary>
    [Fact]
    public void Truncated_pdf_is_InvalidPdf_not_exception()
    {
        byte[] whole = FixtureFactory.CreatePdf(2);
        byte[] truncated = whole[..(whole.Length / 3)];

        PrintStatus status = PdfSource.Load(_engine, truncated, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>
    /// Junk in front of the PDF header never throws. Pinned to what PDFium actually does (measured,
    /// not assumed): it looks for the "%PDF-" header within the first 1024 bytes, so a few bytes or
    /// a few hundred bytes of junk (a stray BOM, an HTTP header, a mail wrapper) still load with
    /// all pages.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(300)]
    public void Leading_junk_within_the_header_window_still_loads(int junkBytes)
    {
        byte[] pdf = [.. new byte[junkBytes].Select(_ => (byte)'x'), .. FixtureFactory.CreatePdf(2)];

        PrintStatus status = PdfSource.Load(_engine, pdf, null, out IPdfDocument? doc);

        using (doc)
        {
            Assert.Equal(PrintStatus.Ok, status);
            Assert.NotNull(doc);
            Assert.Equal(2, doc.PageCount);
        }
    }

    /// <summary>
    /// A well-formed PDF that has no pages is InvalidPdf: there is nothing in it to print. (What
    /// to do with an empty page selection is the facade's concern, not this class's.)
    /// </summary>
    [Fact]
    public void Zero_page_pdf_is_InvalidPdf()
    {
        // PDFsharp cannot save a document without pages, so the smallest valid PDF is written by
        // hand: a catalog and an empty page tree.
        byte[] pdf = BuildZeroPagePdf();

        PrintStatus status = PdfSource.Load(_engine, pdf, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
    }

    /// <summary>
    /// If the engine opens a document with no pages, the document is disposed instead of leaked.
    /// </summary>
    [Fact]
    public void Zero_page_document_from_engine_is_disposed()
    {
        var fake = new FakeEngine(PdfLoadStatus.Ok, new FakeDocument(pageCount: 0));

        PrintStatus status = PdfSource.Load(fake, new byte[] { 1 }, null, out IPdfDocument? doc);

        Assert.Equal(PrintStatus.InvalidPdf, status);
        Assert.Null(doc);
        Assert.True(fake.Document!.IsDisposed);
    }

    /// <summary>
    /// Each engine load status maps to the matching print status, for the real engine and a
    /// protected PDF: no password, an empty password, a wrong one and the right one.
    /// </summary>
    [Fact]
    public void Password_statuses_pass_through()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1, userPassword: UserPassword, ownerPassword: "owner-secret");

        Assert.Equal(PrintStatus.PasswordRequired, PdfSource.Load(_engine, pdf, null, out IPdfDocument? a));
        Assert.Null(a);
        Assert.Equal(PrintStatus.PasswordRequired, PdfSource.Load(_engine, pdf, string.Empty, out IPdfDocument? b));
        Assert.Null(b);
        Assert.Equal(PrintStatus.PasswordInvalid, PdfSource.Load(_engine, pdf, "wrong", out IPdfDocument? c));
        Assert.Null(c);

        PrintStatus ok = PdfSource.Load(_engine, pdf, UserPassword, out IPdfDocument? d);
        using (d)
        {
            Assert.Equal(PrintStatus.Ok, ok);
            Assert.NotNull(d);
        }
    }

    /// <summary>The path and stream overloads pass passwords through the same way.</summary>
    [Fact]
    public void Password_statuses_pass_through_for_streams()
    {
        byte[] pdf = FixtureFactory.CreatePdf(1, userPassword: UserPassword);

        using var noPassword = new MemoryStream(pdf);
        using var wrongPassword = new MemoryStream(pdf);
        using var rightPassword = new MemoryStream(pdf);

        Assert.Equal(PrintStatus.PasswordRequired, PdfSource.Load(_engine, noPassword, null, out _));
        Assert.Equal(PrintStatus.PasswordInvalid, PdfSource.Load(_engine, wrongPassword, "wrong", out _));

        PrintStatus ok = PdfSource.Load(_engine, rightPassword, UserPassword, out IPdfDocument? doc);
        using (doc)
        {
            Assert.Equal(PrintStatus.Ok, ok);
        }
    }

    /// <summary>
    /// Every engine status maps to the right print status. A fake engine reaches the statuses
    /// the real one would need special files for.
    /// </summary>
    [Fact]
    public void Engine_failure_statuses_map_one_to_one()
    {
        // The engine enum is internal, so a theory cannot take it as a parameter: loop instead.
        (PdfLoadStatus Engine, PrintStatus Expected)[] cases =
        [
            (PdfLoadStatus.Invalid, PrintStatus.InvalidPdf),
            (PdfLoadStatus.PasswordRequired, PrintStatus.PasswordRequired),
            (PdfLoadStatus.PasswordInvalid, PrintStatus.PasswordInvalid),
        ];

        foreach ((PdfLoadStatus engineStatus, PrintStatus expected) in cases)
        {
            var fake = new FakeEngine(engineStatus, null);

            PrintStatus status = PdfSource.Load(fake, new byte[] { 1 }, null, out IPdfDocument? doc);

            Assert.Equal(expected, status);
            Assert.Null(doc);
        }
    }

    /// <summary>
    /// An unexpected engine failure, such as a missing pdfium.dll, is not about the caller's file, so it
    /// is not turned into a status here: it reaches the facade with its message, which turns it into
    /// UnknownError (see PdfPrinterTests). No document is handed out.
    /// </summary>
    [Fact]
    public void Engine_exception_reaches_the_caller_with_its_message()
    {
        var fake = new FakeEngine(new DllNotFoundException("pdfium.dll"));
        IPdfDocument? doc = null;

        var thrown = Assert.Throws<DllNotFoundException>(() => PdfSource.Load(fake, new byte[] { 1 }, null, out doc));

        Assert.Equal("pdfium.dll", thrown.Message);
        Assert.Null(doc);
    }

    /// <summary>Builds a structurally valid PDF whose page tree has no pages.</summary>
    private static byte[] BuildZeroPagePdf()
    {
        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void AddObject(string content)
        {
            offsets.Add(body.Length);
            body.Append(offsets.Count).Append(" 0 obj\n").Append(content).Append("\nendobj\n");
        }

        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        AddObject("<< /Type /Pages /Kids [] /Count 0 >>");

        // The cross-reference table lists where each object starts; offsets are byte positions,
        // and the text is pure ASCII so characters and bytes are the same.
        int xrefStart = body.Length;
        body.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            body.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\n");
        body.Append("startxref\n").Append(xrefStart).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>A readable stream that cannot seek and hands data out in small pieces.</summary>
    private sealed class ForwardOnlyStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunkSize), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream whose reads always fail, like a dropped network connection.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The connection was closed.");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>An engine that returns a canned answer, or throws a canned exception.</summary>
    private sealed class FakeEngine : IPdfEngine
    {
        private readonly PdfLoadStatus _status;
        private readonly Exception? _failure;

        public FakeEngine(PdfLoadStatus status, FakeDocument? document)
        {
            _status = status;
            Document = document;
        }

        public FakeEngine(Exception failure) => _failure = failure;

        public FakeDocument? Document { get; }

        public PdfLoadStatus TryLoad(byte[] pdf, string? password, out IPdfDocument? document)
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            document = Document;
            return _status;
        }
    }

    /// <summary>A document with a chosen page count that records whether it was disposed.</summary>
    private sealed class FakeDocument(int pageCount) : IPdfDocument
    {
        public bool IsDisposed { get; private set; }
        public int PageCount { get; } = pageCount;
        public System.Drawing.SizeF GetPageSizePoints(int pageIndex) => throw new NotSupportedException();
        public void RenderToDeviceContext(int pageIndex, IntPtr hdc, System.Drawing.Rectangle destinationPixels, bool forPrinting, int quarterTurns) => throw new NotSupportedException();
        public System.Drawing.Bitmap RenderToBitmap(int pageIndex, int dpi, int quarterTurns) => throw new NotSupportedException();
        public void Dispose() => IsDisposed = true;
    }
}
