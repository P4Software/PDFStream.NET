using System.Text.Json;
using PDFStream.net.TestPrint;
using Xunit;

namespace PDFStream.net.Tests.Sample;

/// <summary>Tests of the test-print sample's logic: settings file, dry run and plain-language messages.</summary>
public class TestPrintTests
{
    private static readonly string[] Printers = { "Microsoft Print to PDF", "Office Laser" };

    /// <summary>Counts print calls and returns a canned result.</summary>
    private sealed class PrintSpy
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public PrintOptions? Options { get; private set; }
        public PrintResult Result { get; init; } = PrintResult.Success(2);

        public PrintResult Print(string path, PrintOptions options)
        {
            Calls++;
            Path = path;
            Options = options;
            return Result;
        }
    }

    private static TestPrintApp NewApp(FakeConsole io, ProgramFolder folder, PrintSpy spy, string[]? printers = null) =>
        new(io, folder.Path, () => printers ?? Printers, spy.Print);

    private static string Settings(string printer = "Office Laser", string pdf = "test.pdf", string extra = "", bool really = false) =>
        $$"""{ "PrinterName": "{{printer}}", "PdfFile": "{{pdf}}", {{extra}} "ReallyPrint": {{(really ? "true" : "false")}} }""";

    [Fact]
    public void Settings_file_is_created_with_comments_when_missing()
    {
        using ProgramFolder folder = new();

        SettingsLoadResult result = SettingsFile.Load(folder.Path);

        Assert.Equal(SettingsOutcome.Created, result.Outcome);
        Assert.True(File.Exists(folder.SettingsPath));
        Assert.Contains(folder.SettingsPath, result.Message);

        string[] lines = File.ReadAllLines(folder.SettingsPath);
        foreach (string key in new[] { "PrinterName", "PdfFile", "Copies", "Pages", "OutputFile", "ReallyPrint" })
        {
            int keyLine = Array.FindIndex(lines, l => l.TrimStart().StartsWith($"\"{key}\":", StringComparison.Ordinal));
            Assert.True(keyLine >= 0, $"Key {key} is missing from the starter file.");

            // The explanation sits above its key and starts with the key name.
            int commentLine = Array.FindIndex(lines, l => l.TrimStart().StartsWith($"// {key}:", StringComparison.Ordinal));
            Assert.True(commentLine >= 0 && commentLine < keyLine, $"Key {key} has no comment line above it.");
        }

        // The file it created reads back with the safe defaults.
        TestPrintSettings back = SettingsFile.Parse(File.ReadAllText(folder.SettingsPath));
        Assert.Equal(string.Empty, back.PrinterName);
        Assert.Equal("test.pdf", back.PdfFile);
        Assert.Equal(1, back.Copies);
        Assert.Equal(string.Empty, back.Pages);
        Assert.Equal(string.Empty, back.OutputFile);
        Assert.False(back.ReallyPrint);

        // A second load reads the file instead of creating it again.
        Assert.Equal(SettingsOutcome.Loaded, SettingsFile.Load(folder.Path).Outcome);
    }

    [Fact]
    public void First_run_tells_the_user_where_the_file_is_lists_printers_and_waits()
    {
        using ProgramFolder folder = new();
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).RunAndWait();

        Assert.Equal(0, exit);
        Assert.Equal(0, spy.Calls);
        Assert.Equal(1, io.KeyWaits);
        Assert.Contains(folder.SettingsPath, io.Text);
        Assert.Contains("Office Laser", io.Text);
        Assert.Contains("double-click", io.Text);
    }

    [Fact]
    public void Settings_with_comments_and_trailing_commas_parse()
    {
        TestPrintSettings s = SettingsFile.Parse("""
            {
              // which printer
              "printername": "Office Laser",   // keys may use any capitalisation
              "PdfFile": "a.pdf",
              "Copies": 3,
              "Pages": "1-2",
              "OutputFile": null,
              "ReallyPrint": true,
            }
            """);

        Assert.Equal("Office Laser", s.PrinterName);
        Assert.Equal("a.pdf", s.PdfFile);
        Assert.Equal(3, s.Copies);
        Assert.Equal("1-2", s.Pages);
        Assert.Equal(string.Empty, s.OutputFile);
        Assert.True(s.ReallyPrint);
    }

    [Theory]
    [InlineData("{ \"PrinterName\": ")]
    [InlineData("not json at all")]
    [InlineData("{ \"Copies\": \"two\" }")]
    [InlineData("{ \"PdfFile\": \"C:\\Docs\\a.pdf\" }")]
    [InlineData("")]
    public void Bad_json_gives_plain_message_not_a_stack_trace(string content)
    {
        using ProgramFolder folder = new();
        folder.WriteSettings(content);
        FakeConsole io = new();

        SettingsLoadResult result = SettingsFile.Load(folder.Path);
        int exit = NewApp(io, folder, new PrintSpy()).RunAndWait();

        Assert.Equal(SettingsOutcome.Invalid, result.Outcome);
        Assert.Equal(1, exit);
        Assert.Equal(1, io.KeyWaits);
        Assert.Contains(folder.SettingsPath, io.Text);
        Assert.Contains("delete the file", io.Text);
        Assert.DoesNotContain("Exception", io.Text);
        Assert.DoesNotContain("   at ", io.Text);
    }

    [Fact]
    public void Relative_pdf_path_is_resolved_next_to_the_exe()
    {
        using ProgramFolder folder = new();
        string expected = folder.CreateFile(Path.Combine("docs", "a.pdf"));
        folder.WriteSettings(Settings(pdf: "docs/a.pdf", really: true));
        PrintSpy spy = new();

        NewApp(new FakeConsole(), folder, spy).Run();

        Assert.Equal(1, spy.Calls);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(spy.Path!));

        // An absolute path is left alone.
        string absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "b.pdf");
        Assert.Equal(absolute, TestPrintApp.ResolveNextToProgram(folder.Path, absolute));
    }

    [Theory]
    [InlineData("\\\"docs/a.pdf\\\"")]
    [InlineData("'docs/a.pdf'")]
    [InlineData("  \\\"docs/a.pdf\\\"  ")]
    public void Quoted_pdf_path_is_unquoted_and_found(string jsonValue)
    {
        using ProgramFolder folder = new();
        string expected = folder.CreateFile(Path.Combine("docs", "a.pdf"));
        folder.WriteSettings($$"""{ "PrinterName": "Office Laser", "PdfFile": "{{jsonValue}}", "ReallyPrint": true }""");
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).Run();

        Assert.Equal(0, exit);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(spy.Path!));
        Assert.Contains("the PDF file was found", io.Text);
    }

    [Theory]
    [InlineData("\\\"out.pdf\\\"")]
    [InlineData("'out.pdf'")]
    public void Quoted_output_file_is_unquoted(string jsonValue)
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings($$"""{ "PrinterName": "Office Laser", "OutputFile": "{{jsonValue}}", "ReallyPrint": true }""");
        PrintSpy spy = new();

        NewApp(new FakeConsole(), folder, spy).Run();

        Assert.Equal(Path.Combine(folder.Path, "out.pdf"), spy.Options!.OutputFile);
    }

    [Fact]
    public void Quoted_printer_name_is_unquoted_and_only_one_pair_is_removed()
    {
        TestPrintSettings s = SettingsFile.Parse("""{ "PrinterName": "\"Office Laser\"", "PdfFile": "\"\"x.pdf\"\"", "OutputFile": "'a" }""");

        Assert.Equal("Office Laser", s.PrinterName);
        Assert.Equal("\"x.pdf\"", s.PdfFile); // one pair only; a leftover quote then shows up in the "not found" message
        Assert.Equal("'a", s.OutputFile);      // quotes that do not match are left alone
    }

    [Fact]
    public void Doubled_quotes_around_a_pasted_path_give_a_hint_about_the_quotes()
    {
        using ProgramFolder folder = new();
        folder.WriteSettings("""{ "PdfFile": ""C:/Docs/a.pdf"" }""");
        FakeConsole io = new();

        int exit = NewApp(io, folder, new PrintSpy()).Run();

        Assert.Equal(1, exit);
        Assert.Contains("extra quote marks", io.Text);
        Assert.Contains("single backslash", io.Text);
    }

    [Fact]
    public void Missing_printer_gives_plain_message_and_list_of_installed_printers()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(printer: "No Such Printer", really: true));
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).RunAndWait();

        Assert.Equal(1, exit);
        Assert.Equal(0, spy.Calls);
        Assert.Contains("No Such Printer", io.Text);
        Assert.Contains("not installed", io.Text);
        Assert.Contains("Microsoft Print to PDF", io.Text);
        Assert.Contains("Office Laser", io.Text);
        Assert.Contains("PrinterName", io.Text);
        Assert.Equal(1, io.KeyWaits);
    }

    [Fact]
    public void Empty_printer_name_lists_the_installed_printers()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(printer: ""));
        FakeConsole io = new();

        int exit = NewApp(io, folder, new PrintSpy()).Run();

        Assert.Equal(1, exit);
        Assert.Contains("PrinterName is empty", io.Text);
        Assert.Contains("Microsoft Print to PDF", io.Text);
        Assert.Contains("Office Laser", io.Text);
    }

    [Fact]
    public void No_installed_printers_says_so_in_plain_language()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings());
        FakeConsole io = new();

        NewApp(io, folder, new PrintSpy(), printers: Array.Empty<string>()).Run();

        Assert.Contains("Windows reports no installed printers", io.Text);
    }

    [Fact]
    public void ReallyPrint_false_does_not_print()
    {
        using ProgramFolder folder = new();
        string pdf = folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(extra: "\"Copies\": 2, \"Pages\": \"1-3\", \"OutputFile\": \"out.pdf\","));
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).RunAndWait();

        Assert.Equal(0, exit);
        Assert.Equal(0, spy.Calls);
        Assert.Equal(1, io.KeyWaits);
        Assert.Contains("DRY RUN", io.Text);
        Assert.Contains("Nothing was printed", io.Text);

        // It shows exactly what would be sent.
        Assert.Contains("Office Laser", io.Text);
        Assert.Contains(pdf, io.Text);
        Assert.Contains("2", io.Text);
        Assert.Contains("1-3", io.Text);
        Assert.Contains(Path.Combine(folder.Path, "out.pdf"), io.Text);

        // And how to really print.
        Assert.Contains("\"ReallyPrint\": true", io.Text);
    }

    [Fact]
    public void Dry_run_reports_a_missing_pdf_and_a_missing_printer_together()
    {
        using ProgramFolder folder = new();
        folder.WriteSettings(Settings(printer: "Gone", pdf: "nope.pdf"));
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).Run();

        Assert.Equal(1, exit);
        Assert.Equal(0, spy.Calls);
        Assert.Contains("PDF file was not found", io.Text);
        Assert.Contains(Path.Combine(folder.Path, "nope.pdf"), io.Text);
        Assert.Contains("\"Gone\" is not installed", io.Text);
    }

    [Fact]
    public void Copies_below_one_is_explained()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(extra: "\"Copies\": 0,", really: true));
        FakeConsole io = new();
        PrintSpy spy = new();

        int exit = NewApp(io, folder, spy).Run();

        Assert.Equal(1, exit);
        Assert.Equal(0, spy.Calls);
        Assert.Contains("Copies must be 1 or more", io.Text);
    }

    [Fact]
    public void ReallyPrint_true_prints_and_shows_message_and_page_count()
    {
        using ProgramFolder folder = new();
        string pdf = folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(extra: "\"Copies\": 2, \"Pages\": \"1-2\",", really: true));
        FakeConsole io = new();
        PrintSpy spy = new() { Result = PrintResult.Success(2) };

        int exit = NewApp(io, folder, spy).RunAndWait();

        Assert.Equal(0, exit);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(pdf, spy.Path);
        Assert.Equal("Office Laser", spy.Options!.PrinterName);
        Assert.Equal(2, spy.Options.Copies);
        Assert.Equal("1-2", spy.Options.Pages);
        Assert.Null(spy.Options.OutputFile);
        Assert.Contains("2 page(s)", io.Text);
        Assert.Contains(spy.Result.Message, io.Text);
        Assert.DoesNotContain("DRY RUN", io.Text);
        Assert.Equal(1, io.KeyWaits);
    }

    [Fact]
    public void ReallyPrint_true_failure_is_shown_in_plain_language()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(really: true));
        FakeConsole io = new();
        PrintSpy spy = new() { Result = PrintResult.Failure(PrintStatus.PrinterUnavailable, "The printer is paused. Resume it and try again.") };

        int exit = NewApp(io, folder, spy).Run();

        Assert.Equal(1, exit);
        Assert.Contains("NOT printed", io.Text);
        Assert.Contains("The printer is paused. Resume it and try again.", io.Text);
        Assert.Contains("0 page(s)", io.Text);
    }

    [Fact]
    public void Unexpected_error_still_waits_for_a_key_and_is_plain()
    {
        using ProgramFolder folder = new();
        folder.CreateFile("test.pdf");
        folder.WriteSettings(Settings(really: true));
        FakeConsole io = new();
        TestPrintApp app = new(io, folder.Path, () => throw new InvalidOperationException("boom"), new PrintSpy().Print);

        int exit = app.RunAndWait();

        Assert.Equal(1, exit);
        Assert.Equal(1, io.KeyWaits);
        Assert.Contains("unexpected", io.Text);
        Assert.Contains("boom", io.Text);
        Assert.DoesNotContain("   at ", io.Text);
    }
}
