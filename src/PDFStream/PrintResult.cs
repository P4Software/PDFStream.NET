namespace PDFStream;

/// <summary>
/// What a print call did. The library does not throw for bad input or printer problems; it returns
/// one of these instead. Check <see cref="Succeeded"/>, and show <see cref="Message"/> to the user.
/// </summary>
public sealed class PrintResult
{
    private PrintResult(PrintStatus status, string message, int pagesPrinted)
    {
        Status = status;
        Message = message;
        PagesPrinted = pagesPrinted;
    }

    /// <summary><see langword="true"/> when the pages were rendered and sent to the printer (<see cref="Status"/> is <see cref="PrintStatus.Ok"/>).</summary>
    public bool Succeeded => Status == PrintStatus.Ok;

    /// <summary>The outcome as a value to branch on.</summary>
    public PrintStatus Status { get; }

    /// <summary>The outcome in plain language: what happened and, for a failure, what to do next. Safe to show to the end user.</summary>
    public string Message { get; }

    /// <summary>
    /// How many pages were sent to the printer. After a failure or a cancellation it is the number
    /// that had already been sent, which may be more than zero.
    /// </summary>
    public int PagesPrinted { get; }

    /// <summary>Creates the result of a job that printed.</summary>
    /// <param name="pagesPrinted">The number of pages sent to the printer.</param>
    internal static PrintResult Success(int pagesPrinted) =>
        new(PrintStatus.Ok, Messages.For(PrintStatus.Ok), pagesPrinted);

    /// <summary>Creates the result of a job that did not (fully) print.</summary>
    /// <param name="status">The reason. It must not be <see cref="PrintStatus.Ok"/>.</param>
    /// <param name="message">A more specific message than the standard one for the status, or <see langword="null"/> for the standard one.</param>
    /// <param name="pagesPrinted">The pages already sent before the failure.</param>
    internal static PrintResult Failure(PrintStatus status, string? message = null, int pagesPrinted = 0)
    {
        // Succeeded is derived from Status, so a "failure" with the Ok status would claim success
        // while the caller meant the opposite. It is a programming error inside the library.
        if (status == PrintStatus.Ok)
        {
            throw new ArgumentException("A failure result cannot have the Ok status.", nameof(status));
        }

        return new PrintResult(status, message ?? Messages.For(status), pagesPrinted);
    }
}
