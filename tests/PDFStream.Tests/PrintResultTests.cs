using Xunit;

namespace PDFStream.Tests;

/// <summary>Tests the plain-language messages and the shape of <see cref="PrintResult"/>.</summary>
public sealed class PrintResultTests
{
    /// <summary>
    /// Every status has a message. A failure message ends with a sentence that starts with an
    /// instruction (what to do next); the Ok message ends with a confirmation.
    /// </summary>
    [Fact]
    public void Every_status_has_a_non_empty_message_ending_with_what_to_do_next_or_confirmation()
    {
        // Words a "what to do next" sentence may open with. A new status with a message that
        // does not end in advice makes this test fail, which is the point.
        string[] instructionOpeners =
        {
            "Check", "Enter", "Ask", "Choose", "Open", "Try", "Install", "Use", "Make", "Contact", "Select", "Fix", "Wait", "Run", "Start",
        };

        foreach (PrintStatus status in Enum.GetValues<PrintStatus>())
        {
            string message = Messages.For(status);

            Assert.False(string.IsNullOrWhiteSpace(message), $"{status} has no message.");
            Assert.EndsWith(".", message);

            string lastSentence = LastSentence(message);
            if (status == PrintStatus.Ok)
            {
                Assert.Contains("sent", lastSentence, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                string firstWord = lastSentence.Split(' ')[0];
                Assert.True(
                    instructionOpeners.Contains(firstWord),
                    $"{status}: the last sentence should tell the user what to do next, but it is \"{lastSentence}\".");
            }
        }
    }

    /// <summary>A message is its own text, not a copy of another status's text.</summary>
    [Fact]
    public void Every_status_has_its_own_message()
    {
        string[] messages = Enum.GetValues<PrintStatus>().Select(Messages.For).ToArray();

        Assert.Equal(messages.Length, messages.Distinct().Count());
    }

    /// <summary>The success result carries the Ok message and the page count, and reports success.</summary>
    [Fact]
    public void Success_result_reports_pages_and_Ok()
    {
        PrintResult result = PrintResult.Success(3);

        Assert.True(result.Succeeded);
        Assert.Equal(PrintStatus.Ok, result.Status);
        Assert.Equal(3, result.PagesPrinted);
        Assert.Equal(Messages.For(PrintStatus.Ok), result.Message);
    }

    /// <summary>A failure result is not a success and uses the status's message unless a more specific one is given.</summary>
    [Fact]
    public void Failure_result_is_not_a_success()
    {
        PrintResult result = PrintResult.Failure(PrintStatus.PrinterNotFound);
        PrintResult partial = PrintResult.Failure(PrintStatus.Cancelled, pagesPrinted: 2);
        PrintResult custom = PrintResult.Failure(PrintStatus.UnknownError, "Custom text.");

        Assert.False(result.Succeeded);
        Assert.Equal(PrintStatus.PrinterNotFound, result.Status);
        Assert.Equal(0, result.PagesPrinted);
        Assert.Equal(Messages.For(PrintStatus.PrinterNotFound), result.Message);
        Assert.Equal(2, partial.PagesPrinted);
        Assert.Equal("Custom text.", custom.Message);
    }

    /// <summary>A failure can never be built with the Ok status, so Succeeded and Status cannot disagree.</summary>
    [Fact]
    public void Failure_with_Ok_status_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PrintResult.Failure(PrintStatus.Ok));
    }

    /// <summary>The options default to what the design promises: one copy, the printer's own colour and duplex, fit to page.</summary>
    [Fact]
    public void Options_have_the_documented_defaults()
    {
        var options = new PrintOptions();

        Assert.Equal(1, options.Copies);
        Assert.Null(options.Color);
        Assert.Equal(ScaleMode.FitToPage, options.Scale);
        Assert.Equal(DuplexMode.PrinterDefault, options.Duplex);
        Assert.Null(options.Pages);
        Assert.Null(options.Password);
        Assert.Null(options.DocumentName);
        Assert.Null(options.PaperSource);
        Assert.Null(options.OutputFile);
        Assert.Equal(string.Empty, options.PrinterName);
    }

    /// <summary>
    /// A custom message that is empty or only spaces would show the user a blank line, so the standard
    /// message for the status is used instead; a real custom message is kept.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Blank_custom_message_falls_back_to_the_standard_message(string blank)
    {
        PrintResult result = PrintResult.Failure(PrintStatus.PrinterNotFound, blank);

        Assert.Equal(Messages.For(PrintStatus.PrinterNotFound), result.Message);
    }

    /// <summary>
    /// The generic "printer unavailable" text is what a stopped Print Spooler produces when the printer
    /// list itself cannot be read, so it must point at the spooler.
    /// </summary>
    [Fact]
    public void PrinterUnavailable_message_hints_at_the_Print_Spooler()
    {
        Assert.Contains("Print Spooler", Messages.PrinterUnavailable());
    }

    /// <summary>
    /// Under the default fit-to-page, "fit the page to the paper" is advice the caller already follows,
    /// so the too-large advice must name something else: a smaller paper size or splitting the document.
    /// </summary>
    [Fact]
    public void PageTooLarge_advice_is_not_circular_and_does_not_promise_a_withdrawn_job()
    {
        string message = Messages.PageTooLarge(3);

        Assert.DoesNotContain("fitted to the paper", message);
        Assert.Contains("smaller paper size", message);
        Assert.Contains("split", message);
        Assert.DoesNotContain("job was withdrawn", message);
        Assert.Contains("may have been withdrawn", message);
        Assert.Contains("print queue", message);
    }

    /// <summary>The page-failure text says the job may have been withdrawn and tells the user to check the queue.</summary>
    [Fact]
    public void PageFailed_message_does_not_promise_a_withdrawn_job()
    {
        string message = Messages.PageFailed(2, "boom");

        Assert.DoesNotContain("job was withdrawn", message);
        Assert.Contains("may have been withdrawn", message);
        Assert.Contains("check the print queue", message, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Details: boom", message);
    }

    // The text after the last ". " (or the whole message if it is a single sentence).
    private static string LastSentence(string message)
    {
        string body = message.TrimEnd('.');
        int cut = body.LastIndexOf(". ", StringComparison.Ordinal);
        return cut < 0 ? body : body[(cut + 2)..];
    }
}
