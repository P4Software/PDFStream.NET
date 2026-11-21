using PDFStream.NET.Printing;

namespace PDFStream.NET.Tests.Printing;

/// <summary>
/// A printer status probe that returns a fixed reading, or throws, so every queue state can be tested
/// without pausing or unplugging a real printer.
/// </summary>
internal sealed class FakeStatusProbe : IPrinterStatusProbe
{
    private readonly PrinterStatusReadOutcome _outcome;
    private readonly uint _status;
    private readonly uint _attributes;

    /// <summary>Creates a probe that reports the given outcome and raw winspool values.</summary>
    public FakeStatusProbe(PrinterStatusReadOutcome outcome, uint status = 0, uint attributes = 0)
    {
        _outcome = outcome;
        _status = status;
        _attributes = attributes;
    }

    /// <summary>When set, reading the status throws this instead.</summary>
    public Exception? Throws { get; init; }

    /// <summary>The printer names asked about, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>What the spooler check reports: <see langword="true"/> stands for a stopped Print Spooler.</summary>
    public bool SpoolerStopped { get; init; }

    /// <summary>When set, the spooler check throws this instead of answering.</summary>
    public Exception? SpoolerCheckThrows { get; init; }

    /// <summary>How many times the spooler check was made.</summary>
    public int SpoolerChecks { get; private set; }

    /// <summary>A probe that reads these status and attribute bits.</summary>
    public static FakeStatusProbe Reading(uint status, uint attributes = 0) => new(PrinterStatusReadOutcome.Read, status, attributes);

    /// <inheritdoc/>
    public PrinterStatusReadOutcome TryRead(string printerName, out uint status, out uint attributes)
    {
        Asked.Add(printerName);
        if (Throws is not null)
        {
            throw Throws;
        }

        status = _status;
        attributes = _attributes;
        return _outcome;
    }

    /// <summary>Reports <see cref="SpoolerStopped"/>, or throws <see cref="SpoolerCheckThrows"/>.</summary>
    public bool IsSpoolerStopped()
    {
        SpoolerChecks++;
        return SpoolerCheckThrows is null ? SpoolerStopped : throw SpoolerCheckThrows;
    }
}
