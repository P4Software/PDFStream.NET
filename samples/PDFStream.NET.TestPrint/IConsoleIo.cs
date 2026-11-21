namespace PDFStream.NET.TestPrint;

/// <summary>
/// The only way the test-print program talks to the person: lines of plain text out and one
/// key press in. Keeping the console behind this small interface lets the tests run the whole
/// program without a real console, so no test can ever block waiting for a key.
/// </summary>
public interface IConsoleIo
{
    /// <summary>Shows one line of text. An empty text shows an empty line.</summary>
    /// <param name="text">The text to show.</param>
    void WriteLine(string text = "");

    /// <summary>Shows a short prompt and waits until the person presses a key.</summary>
    void WaitForKey();
}

/// <summary>The real console: <see cref="Console"/> output and a real key press.</summary>
public sealed class SystemConsoleIo : IConsoleIo
{
    /// <summary>Creates the console wrapper and switches the output to UTF-8 where Windows allows it.</summary>
    public SystemConsoleIo()
    {
        try
        {
            // Printer names can contain accents or other non-ASCII letters; the default console code
            // page would show them as question marks, and then the name could not be copied.
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // No real console is attached (output is redirected); the text is shown as well as it can be.
        }
    }

    /// <inheritdoc />
    public void WriteLine(string text = "") => Console.WriteLine(text);

    /// <inheritdoc />
    public void WaitForKey()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close this window...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // The input is redirected (for example the program was started from a script), so there
            // is no key to read. A line read keeps the "wait before closing" promise as well as possible.
            Console.ReadLine();
        }
    }
}
