using PDFStream.NET.TestPrint;

namespace PDFStream.NET.Tests.Sample;

/// <summary>A console that records what was shown and counts key waits, so a test never blocks.</summary>
internal sealed class FakeConsole : IConsoleIo
{
    private readonly List<string> _lines = new();

    /// <summary>How many times the program asked for a key press.</summary>
    public int KeyWaits { get; private set; }

    /// <summary>Everything shown so far, one line per row.</summary>
    public string Text => string.Join(Environment.NewLine, _lines);

    /// <inheritdoc />
    public void WriteLine(string text = "") => _lines.Add(text);

    /// <inheritdoc />
    public void WaitForKey() => KeyWaits++;
}

/// <summary>A scratch folder standing in for "the folder of the exe"; deleted when the test ends.</summary>
internal sealed class ProgramFolder : IDisposable
{
    /// <summary>Creates a new empty folder under the system temp folder.</summary>
    public ProgramFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pdfstream.net-sample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder path.</summary>
    public string Path { get; }

    /// <summary>The settings file inside the folder.</summary>
    public string SettingsPath => System.IO.Path.Combine(Path, SettingsFile.FileName);

    /// <summary>Writes a settings file with the given text.</summary>
    public void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    /// <summary>Creates a placeholder PDF (the print step is replaced in tests, so its content is never read).</summary>
    public string CreateFile(string relativePath)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "%PDF-1.4 placeholder");
        return full;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless; never fail a test because of cleanup.
        }
    }
}
