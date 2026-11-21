using System.Text.Json;

namespace PDFStream.NET.TestPrint;

/// <summary>
/// What the person can set in <c>settings.json</c>. Every property has a safe default, so a file that
/// leaves a key out still works, and the default of <see cref="ReallyPrint"/> is the safe one: a dry run.
/// </summary>
public sealed class TestPrintSettings
{
    /// <summary>The printer to print to, exactly as Windows lists it. Empty means "show me the list of printers".</summary>
    public string PrinterName { get; init; } = string.Empty;

    /// <summary>The PDF to print. A relative path is resolved next to the program.</summary>
    public string PdfFile { get; init; } = "test.pdf";

    /// <summary>How many copies to print; must be at least 1.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>The pages to print, for example <c>1-3, 5</c>. Empty means every page.</summary>
    public string Pages { get; init; } = string.Empty;

    /// <summary>The file to write instead of printing on paper, for printers such as Microsoft Print to PDF. Empty means none.</summary>
    public string OutputFile { get; init; } = string.Empty;

    /// <summary>
    /// <see langword="false"/> (the default) is a dry run that only checks and reports; <see langword="true"/>
    /// really sends the PDF to the printer.
    /// </summary>
    public bool ReallyPrint { get; init; }
}

/// <summary>How reading <c>settings.json</c> ended.</summary>
public enum SettingsOutcome
{
    /// <summary>The file existed and was read.</summary>
    Loaded,

    /// <summary>The file did not exist, so a commented starter file was created. Nothing else should run yet.</summary>
    Created,

    /// <summary>The file could not be read or created; <see cref="SettingsLoadResult.Message"/> says why and what to do.</summary>
    Invalid,
}

/// <summary>The result of <see cref="SettingsFile.Load"/>.</summary>
/// <param name="Outcome">How reading ended.</param>
/// <param name="Settings">The settings; only set when <paramref name="Outcome"/> is <see cref="SettingsOutcome.Loaded"/>.</param>
/// <param name="Message">A plain-language explanation for <see cref="SettingsOutcome.Created"/> and <see cref="SettingsOutcome.Invalid"/>; empty when loaded.</param>
/// <param name="Path">The full path of <c>settings.json</c>.</param>
public sealed record SettingsLoadResult(SettingsOutcome Outcome, TestPrintSettings? Settings, string Message, string Path);

/// <summary>Reads <c>settings.json</c> next to the program and creates a commented starter file when it is missing.</summary>
public static class SettingsFile
{
    /// <summary>The file name, always looked up next to the program.</summary>
    public const string FileName = "settings.json";

    /// <summary>The names of every setting, in the order they appear in the starter file.</summary>
    public static readonly IReadOnlyList<string> Keys =
        new[] { "PrinterName", "PdfFile", "Copies", "Pages", "OutputFile", "ReallyPrint" };

    /// <summary>
    /// The text written on the first run. It is a plain string (not a serialised object) because JSON
    /// has no comments of its own and the comments are the documentation for the person editing it.
    /// </summary>
    public const string StarterContent = """
        {
          // PDFStream.NET test-print settings.
          // Lines that start with // are comments: they explain each setting and are ignored.
          // Save this file, then double-click PDFStream.NET.TestPrint.exe again.
          // Windows paths: write them with forward slashes (C:/Docs/a.pdf) or double backslashes (C:\\Docs\\a.pdf).

          // PrinterName: the printer to use, exactly as Windows lists it.
          // Leave it empty and run the program: it lists every installed printer, and you copy one name here.
          "PrinterName": "",

          // PdfFile: the PDF to print. A path without a folder (like test.pdf) is looked for next to this program.
          // Explorer's "Copy as path" adds quote marks around the path: the program removes them, but if the
          // file then will not load, remove the doubled quote marks yourself ("C:/Docs/a.pdf", not ""C:/Docs/a.pdf"").
          "PdfFile": "test.pdf",

          // Copies: how many copies to print. A whole number, 1 or more.
          "Copies": 1,

          // Pages: which pages to print, for example "1-3, 5". Leave it empty ("") to print every page.
          "Pages": "",

          // OutputFile: for printers that can print into a file, such as "Microsoft Print to PDF".
          // Give the file to create, for example "result.pdf" (saved next to this program). Leave empty ("") for none.
          "OutputFile": "",

          // ReallyPrint: false (the default) is a DRY RUN. The program only checks your settings and shows
          // what it would send, and nothing is printed. Change it to true to really send the PDF to the printer.
          "ReallyPrint": false
        }
        """;

    /// <summary>
    /// Reads the options with the leniency a hand-edited file needs: // comments are skipped, a comma
    /// after the last item is accepted and key names may use any capitalisation.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads the text of a settings file. Throws <see cref="JsonException"/> when the text is not valid settings.</summary>
    /// <param name="json">The text of the file.</param>
    /// <returns>The settings, with defaults for anything left out.</returns>
    public static TestPrintSettings Parse(string json)
    {
        TestPrintSettings? read = JsonSerializer.Deserialize<TestPrintSettings>(json, ReadOptions);
        if (read is null)
        {
            // A file that holds only the word null deserialises to nothing at all.
            throw new JsonException("The file does not contain any settings.");
        }

        // A key set to null in the file bypasses the property defaults; fall back to them here so the
        // rest of the program never meets a null text.
        TestPrintSettings defaults = new();
        return new TestPrintSettings
        {
            PrinterName = Unquote(read.PrinterName ?? defaults.PrinterName),
            PdfFile = Unquote(read.PdfFile ?? defaults.PdfFile),
            Copies = read.Copies,
            Pages = (read.Pages ?? defaults.Pages).Trim(),
            OutputFile = Unquote(read.OutputFile ?? defaults.OutputFile),
            ReallyPrint = read.ReallyPrint,
        };
    }

    /// <summary>
    /// Trims a text and removes ONE pair of matching surrounding quotes (double or single). Explorer's
    /// "Copy as path" puts double quotes around a path, and people paste them into the file as they are;
    /// a path that really starts with a quote character does not exist, so removing them is always safe.
    /// </summary>
    /// <param name="value">The text from the settings file.</param>
    /// <returns>The text without whitespace around it and without one pair of surrounding quotes.</returns>
    private static string Unquote(string value)
    {
        string text = value.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    /// <summary>Loads <c>settings.json</c> from the folder, creating the commented starter file first if it is missing.</summary>
    /// <param name="folder">The folder of the program.</param>
    /// <returns>What happened, with a plain-language message when the program cannot go on.</returns>
    public static SettingsLoadResult Load(string folder)
    {
        string path = System.IO.Path.Combine(folder, FileName);

        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, StarterContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SettingsLoadResult(SettingsOutcome.Invalid, null,
                    $"There is no settings file yet, and I could not create one at {path} because Windows does not allow writing to that folder. " +
                    "Copy the whole program folder to a place you can write to, such as your Desktop, and double-click the program there.", path);
            }

            return new SettingsLoadResult(SettingsOutcome.Created, null,
                $"This is the first run, so there was no settings file yet. I created one for you at:{Environment.NewLine}  {path}", path);
        }

        try
        {
            return new SettingsLoadResult(SettingsOutcome.Loaded, Parse(File.ReadAllText(path)), string.Empty, path);
        }
        catch (JsonException ex)
        {
            // Only the line number is shown, never the exception text or a stack trace: they mean nothing to the reader.
            string where = ex.LineNumber is long line ? $" Look near line {line + 1}." : string.Empty;
            return new SettingsLoadResult(SettingsOutcome.Invalid, null,
                $"I could not read your settings file at {path}.{where} " +
                "Check that every text is in double quotes, that Copies is a plain number, that ReallyPrint is true or false without quotes, " +
                "that Windows paths use forward slashes or doubled backslashes (a single backslash is not allowed), " +
                "and that a path pasted from Explorer does not have extra quote marks around it (remove the doubled quotes). " +
                "To start again, delete the file and run the program: it creates a fresh one.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(SettingsOutcome.Invalid, null,
                $"I could not open your settings file at {path}. Close any program that has it open and try again.", path);
        }
    }
}
