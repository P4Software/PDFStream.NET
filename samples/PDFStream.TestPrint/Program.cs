namespace PDFStream.TestPrint;

/// <summary>
/// The entry point. Double-clicking the program runs it with no switches: the real console, the real
/// printer list and the PDFStream library are handed to <see cref="TestPrintApp"/>, which does the work.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            return new TestPrintApp(
                new SystemConsoleIo(),
                AppContext.BaseDirectory,
                TestPrintApp.ListInstalledPrinters,
                (path, options) => PdfPrinter.Print(path, options)).RunAndWait();
        }
        catch (Exception ex)
        {
            // Only reached if even setting up failed. The window must still stay open long enough to read this.
            Console.WriteLine("Something unexpected went wrong before the program could start, and nothing was printed.");
            Console.WriteLine($"What Windows reported: {ex.Message}");
            Console.WriteLine("Please try again. If it keeps happening, send this text to whoever looks after this program.");
            new SystemConsoleIo().WaitForKey();
            return 1;
        }
    }
}
