using System.Drawing.Printing;

namespace PDFStream.net.TestPrint;

/// <summary>
/// The sending step of the program. In the real program it is <see cref="PdfPrinter.Print(string, PrintOptions, System.Threading.CancellationToken)"/>;
/// the tests pass their own so they can prove that a dry run never sends anything.
/// </summary>
/// <param name="pdfPath">The full path of the PDF.</param>
/// <param name="options">What and where to print.</param>
/// <returns>What the library reported.</returns>
public delegate PrintResult PrintAction(string pdfPath, PrintOptions options);

/// <summary>
/// The whole logic of the double-click test-print program: read <c>settings.json</c>, check the printer
/// and the PDF, then either report what would happen (a dry run, the default) or really print. All
/// output goes through <see cref="IConsoleIo"/>, and every message says what happened and what to do next.
/// </summary>
public sealed class TestPrintApp
{
    private readonly IConsoleIo _io;
    private readonly string _folder;
    private readonly Func<IReadOnlyList<string>> _installedPrinters;
    private readonly PrintAction _print;

    /// <summary>Creates the program logic.</summary>
    /// <param name="io">Where the text goes, and where the key press comes from.</param>
    /// <param name="folder">The folder of the program: <c>settings.json</c> lives here and relative paths start here.</param>
    /// <param name="installedPrinters">Lists the printers Windows has installed.</param>
    /// <param name="print">Sends the PDF to the printer.</param>
    public TestPrintApp(IConsoleIo io, string folder, Func<IReadOnlyList<string>> installedPrinters, PrintAction print)
    {
        _io = io;
        _folder = folder;
        _installedPrinters = installedPrinters;
        _print = print;
    }

    /// <summary>The printers installed on this computer, as <see cref="PrinterSettings.InstalledPrinters"/> lists them.</summary>
    /// <returns>The names; an empty list when Windows cannot say (for example the Print Spooler is stopped).</returns>
    public static IReadOnlyList<string> ListInstalledPrinters()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            // Treated like "no printers": the caller then explains it in plain language.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Makes a path usable whatever folder the program was started from: a relative path is taken
    /// relative to the program's own folder (a double-click and a shortcut start in different folders),
    /// and a full path is returned unchanged.
    /// </summary>
    /// <param name="folder">The folder of the program.</param>
    /// <param name="path">The path from the settings file.</param>
    /// <returns>The full path.</returns>
    public static string ResolveNextToProgram(string folder, string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(folder, path));

    /// <summary>
    /// Runs the program and always waits for a key press at the end, even after an unexpected error,
    /// so the window never closes before the result can be read.
    /// </summary>
    /// <returns>0 when nothing is wrong (including a dry run), 1 when the person has something to fix.</returns>
    public int RunAndWait()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            // The last safety net. The library itself never throws for bad input, but reading files and
            // listing printers can still fail in ways nobody planned for; the person gets words, not a stack trace.
            string detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            _io.WriteLine();
            _io.WriteLine("Something unexpected went wrong, and nothing was printed.");
            _io.WriteLine($"What Windows reported: {detail}");
            _io.WriteLine("Please try again. If it keeps happening, send this text to whoever looks after this program.");
            return 1;
        }
        finally
        {
            _io.WaitForKey();
        }
    }

    /// <summary>Runs the program once, without waiting for a key. Use <see cref="RunAndWait"/> from the entry point.</summary>
    /// <returns>0 when nothing is wrong (including a dry run), 1 when the person has something to fix.</returns>
    public int Run()
    {
        _io.WriteLine("PDFStream.net test print");
        _io.WriteLine("--------------------");

        SettingsLoadResult loaded = SettingsFile.Load(_folder);
        if (loaded.Outcome == SettingsOutcome.Invalid)
        {
            _io.WriteLine(loaded.Message);
            return 1;
        }

        IReadOnlyList<string> printers = _installedPrinters();

        if (loaded.Outcome == SettingsOutcome.Created)
        {
            _io.WriteLine(loaded.Message);
            _io.WriteLine();
            ShowPrinters(printers);
            _io.WriteLine();
            _io.WriteLine("What to do next:");
            _io.WriteLine("  1. Open that file in Notepad (right-click it, then Open with).");
            _io.WriteLine("  2. Copy your printer's name into PrinterName and set PdfFile to the PDF you want to print.");
            _io.WriteLine("  3. Save the file and double-click this program again.");
            _io.WriteLine("Nothing was printed. The file explains every setting.");
            return 0;
        }

        TestPrintSettings settings = loaded.Settings!;
        _io.WriteLine($"Checking your settings in {loaded.Path}");

        // Every check is reported, not just the first failure, so the person can fix all of it in one edit.
        bool problems = false;

        // The printer name decides what the library is given; an installed printer is matched
        // ignoring capitalisation and the name is then passed exactly as Windows spells it.
        string? printer = null;
        if (settings.PrinterName.Length == 0)
        {
            problems = true;
            _io.WriteLine("  Problem: PrinterName is empty in settings.json, so I do not know which printer to use.");
            ShowPrinters(printers);
            _io.WriteLine("  Copy one of the names above exactly into PrinterName in settings.json.");
        }
        else
        {
            printer = printers.FirstOrDefault(p => string.Equals(p, settings.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (printer is null)
            {
                problems = true;
                _io.WriteLine($"  Problem: the printer \"{settings.PrinterName}\" is not installed on this computer.");
                ShowPrinters(printers);
                _io.WriteLine("  Copy one of the names above exactly into PrinterName in settings.json.");
            }
            else
            {
                _io.WriteLine($"  OK: the printer \"{printer}\" is installed.");
            }
        }

        string pdfPath = string.Empty;
        if (settings.PdfFile.Length == 0)
        {
            problems = true;
            _io.WriteLine("  Problem: PdfFile is empty in settings.json, so I do not know which PDF to print. Set PdfFile to the PDF you want to print.");
        }
        else
        {
            pdfPath = ResolveNextToProgram(_folder, settings.PdfFile);
            if (File.Exists(pdfPath))
            {
                _io.WriteLine($"  OK: the PDF file was found: {pdfPath}");
            }
            else
            {
                problems = true;
                _io.WriteLine($"  Problem: the PDF file was not found: {pdfPath}");
                _io.WriteLine("  Put the PDF there, or change PdfFile in settings.json to where it really is.");
            }
        }

        if (settings.Copies < 1)
        {
            problems = true;
            _io.WriteLine($"  Problem: Copies must be 1 or more, but settings.json says {settings.Copies}. Change Copies to a whole number such as 1.");
        }

        if (problems || printer is null)
        {
            _io.WriteLine();
            _io.WriteLine($"Nothing was printed. Fix the problems above in {loaded.Path}, save it and double-click this program again.");
            return 1;
        }

        string? outputFile = settings.OutputFile.Length == 0 ? null : ResolveNextToProgram(_folder, settings.OutputFile);
        string pages = settings.Pages.Length == 0 ? "all pages" : settings.Pages;

        if (!settings.ReallyPrint)
        {
            _io.WriteLine();
            _io.WriteLine("This is a DRY RUN: ReallyPrint is false in settings.json, so nothing was sent to the printer.");
            _io.WriteLine("This is exactly what would be sent:");
            _io.WriteLine($"  Printer:     {printer}");
            _io.WriteLine($"  PDF file:    {pdfPath}");
            _io.WriteLine($"  Copies:      {settings.Copies}");
            _io.WriteLine($"  Pages:       {pages}");
            _io.WriteLine($"  Output file: {outputFile ?? "(none, it goes to the printer)"}");
            _io.WriteLine();
            _io.WriteLine("Nothing was printed. To really print, open settings.json, change \"ReallyPrint\": false to \"ReallyPrint\": true,");
            _io.WriteLine("save the file and double-click this program again.");
            return 0;
        }

        _io.WriteLine();
        _io.WriteLine($"Sending {Path.GetFileName(pdfPath)} to \"{printer}\" (ReallyPrint is true)...");

        // Public API only, exactly as a customer would call it. The library never throws for bad
        // input or printer trouble: whatever goes wrong comes back as a status and a plain message.
        PrintResult result = _print(pdfPath, new PrintOptions
        {
            PrinterName = printer,
            DocumentName = Path.GetFileName(pdfPath),
            Copies = settings.Copies,
            Pages = settings.Pages.Length == 0 ? null : settings.Pages,
            OutputFile = outputFile,
        });

        _io.WriteLine();
        if (result.Succeeded)
        {
            _io.WriteLine($"Printed: {result.PagesPrinted} page(s) were sent to \"{printer}\".");
            if (outputFile is not null)
            {
                _io.WriteLine($"The printer was asked to write the result to: {outputFile}");
            }

            _io.WriteLine(result.Message);
            return 0;
        }

        _io.WriteLine($"The document was NOT printed: {result.PagesPrinted} page(s) were sent.");
        _io.WriteLine(result.Message);
        return 1;
    }

    /// <summary>Shows the installed printers, or explains that there are none.</summary>
    /// <param name="printers">The installed printer names.</param>
    private void ShowPrinters(IReadOnlyList<string> printers)
    {
        if (printers.Count == 0)
        {
            _io.WriteLine("  Windows reports no installed printers. Install a printer in Windows settings (or check that the Print Spooler service is running), then try again.");
            return;
        }

        _io.WriteLine("  These printers are installed on this computer:");
        foreach (string name in printers)
        {
            _io.WriteLine($"    {name}");
        }
    }
}
