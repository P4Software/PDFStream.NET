using System.Drawing.Printing;

namespace PDFStream.Printing;

/// <summary>
/// Finds the requested printer and turns <see cref="PrintOptions"/> into a ready-to-use
/// <see cref="PrinterSettings"/> (copies, duplex, colour, paper source, print to file). Nothing in
/// here prints, opens a dialog or writes a file; it only prepares and validates settings, and every
/// problem comes back as a status and a plain-language message instead of an exception.
/// </summary>
internal static class PrinterLocator
{
    /// <summary>The largest copy count Windows can store in a print job (a 16-bit number).</summary>
    private const int MaxCopies = short.MaxValue;

    /// <summary>
    /// Checks the options that need no printer to be judged: a printer name, a copy count Windows can
    /// store, and enum values that are defined. The public print method calls it before it opens the
    /// PDF, so a wrong option is reported without any file or printer work.
    /// </summary>
    /// <param name="options">The caller's print options; <see langword="null"/> is not usable.</param>
    /// <returns><see langword="true"/> when the options can be used.</returns>
    internal static bool AreOptionsUsable(PrintOptions? options) =>
        options is not null
        && !string.IsNullOrWhiteSpace(options.PrinterName)
        && options.Copies >= 1
        && options.Copies <= MaxCopies
        && Enum.IsDefined(options.Duplex)
        && Enum.IsDefined(options.Scale);

    /// <summary>
    /// Prepares the settings for one print job on the real installed printers.
    /// </summary>
    /// <param name="options">The caller's print options.</param>
    /// <param name="settings">The ready settings when the result is <see cref="PrintStatus.Ok"/>, otherwise <see langword="null"/>.</param>
    /// <returns>A status; use the overload with a message when the text matters (the paper source text lists the available trays).</returns>
    internal static PrintStatus TryCreateSettings(PrintOptions options, out PrinterSettings? settings) =>
        TryCreateSettings(options, SystemPrinterCatalog.Instance, WinspoolPrinterStatusProbe.Instance, out settings, out _);

    /// <summary>
    /// Prepares the settings for one print job.
    /// </summary>
    /// <param name="options">The caller's print options.</param>
    /// <param name="catalog">Where the installed printer names come from.</param>
    /// <param name="settings">The ready settings when the result is <see cref="PrintStatus.Ok"/>, otherwise <see langword="null"/>.</param>
    /// <param name="message">The plain-language text for the result: it says what happened and what to do next.</param>
    /// <returns>
    /// <see cref="PrintStatus.Ok"/>, <see cref="PrintStatus.InvalidOptions"/> (empty name, fewer than one
    /// copy, unknown paper source, unusable output file), <see cref="PrintStatus.PrinterNotFound"/> or
    /// <see cref="PrintStatus.PrinterUnavailable"/> (the printer cannot be opened, cannot do what was
    /// asked, or its queue is paused, set to "Use printer offline", not available or being deleted; the
    /// message names the cause). The queue state is read with the real Windows print spooler. An
    /// "offline" report alone does not refuse the printer (see <see cref="PrinterStatusCheck"/>).
    /// </returns>
    internal static PrintStatus TryCreateSettings(
        PrintOptions options,
        IPrinterCatalog catalog,
        out PrinterSettings? settings,
        out string message) =>
        TryCreateSettings(options, catalog, WinspoolPrinterStatusProbe.Instance, out settings, out message);

    /// <summary>
    /// Prepares the settings for one print job, checking the printer queue's state with the given probe.
    /// </summary>
    /// <param name="options">The caller's print options.</param>
    /// <param name="catalog">Where the installed printer names come from.</param>
    /// <param name="statusProbe">Where the printer queue's state comes from (see <see cref="PrinterStatusCheck"/>).</param>
    /// <param name="settings">The ready settings when the result is <see cref="PrintStatus.Ok"/>, otherwise <see langword="null"/>.</param>
    /// <param name="message">The plain-language text for the result: it says what happened and what to do next.</param>
    /// <returns>As for the overload without a probe.</returns>
    internal static PrintStatus TryCreateSettings(
        PrintOptions options,
        IPrinterCatalog catalog,
        IPrinterStatusProbe statusProbe,
        out PrinterSettings? settings,
        out string message)
    {
        settings = null;

        // Check the options before touching the print system: bad input should fail the same way
        // on a machine with no printers at all.
        if (!AreOptionsUsable(options))
        {
            message = Messages.InvalidOptions();
            return PrintStatus.InvalidOptions;
        }

        string? outputFile = null;
        if (options.OutputFile is not null)
        {
            outputFile = ResolveOutputFile(options.OutputFile);
            if (outputFile is null)
            {
                message = Messages.InvalidOutputFile(options.OutputFile);
                return PrintStatus.InvalidOptions;
            }
        }

        string? installedName;
        try
        {
            installedName = FindInstalledName(catalog, options.PrinterName);
        }
        catch (Exception)
        {
            // The print system itself is unreachable, typically a stopped Print Spooler service.
            message = Messages.PrinterUnavailable();
            return PrintStatus.PrinterUnavailable;
        }

        if (installedName is null)
        {
            message = Messages.PrinterNotFound();
            return PrintStatus.PrinterNotFound;
        }

        PrinterSettings candidate;
        try
        {
            candidate = new PrinterSettings { PrinterName = installedName };

            // IsValid is false when Windows knows the name but cannot open the printer (it was
            // deleted a moment ago, the spooler refuses access to this account, and so on).
            if (!candidate.IsValid)
            {
                message = Messages.PrinterUnavailable();
                return PrintStatus.PrinterUnavailable;
            }

            // A paused queue (or one set to "Use printer offline") would accept the job and hold it, and
            // the caller would be told it printed. Checked here, where every print call gets its settings, so the job is never
            // started; the check itself never throws and never blocks on a status it cannot read.
            PrintStatus ready = PrinterStatusCheck.Check(statusProbe, installedName, out message);
            if (ready != PrintStatus.Ok)
            {
                return ready;
            }

            PrintStatus status = Apply(options, candidate, outputFile, out message);
            if (status != PrintStatus.Ok)
            {
                return status;
            }
        }
        catch (Exception)
        {
            // PrinterSettings reports a printer that misbehaves through several exception types
            // (InvalidPrinterException, Win32Exception, ...). The public API must not leak them.
            message = Messages.PrinterUnavailable();
            return PrintStatus.PrinterUnavailable;
        }

        settings = candidate;
        message = Messages.Ok();
        return PrintStatus.Ok;
    }

    /// <summary>
    /// Finds the installed printer the caller means and returns its name exactly as Windows lists
    /// it. An exact match wins; otherwise the match ignores letter case (Windows printer names are
    /// not case sensitive) and ignores spaces around the name, which a settings file easily adds.
    /// Network names such as <c>\\server\printer</c> are ordinary names for this purpose.
    /// </summary>
    /// <param name="catalog">Where the installed printer names come from.</param>
    /// <param name="requested">The name the caller asked for.</param>
    /// <returns>The installed name, or <see langword="null"/> when no printer matches.</returns>
    internal static string? FindInstalledName(IPrinterCatalog catalog, string requested)
    {
        IReadOnlyList<string> installed = catalog.GetInstalledPrinterNames();
        string wanted = requested.Trim();

        // Ordinal comparisons: culture rules (Turkish dotless i, for example) must never decide
        // which printer receives a job. OrdinalIgnoreCase still folds accented letters such as
        // e-acute and E-acute together.
        foreach (string name in installed)
        {
            if (string.Equals(name, wanted, StringComparison.Ordinal))
            {
                return name;
            }
        }

        foreach (string name in installed)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Maps the library's duplex choice to the one Windows Forms understands.</summary>
    /// <param name="mode">The duplex mode from the print options.</param>
    /// <returns>
    /// The matching <see cref="Duplex"/>, or <see cref="Duplex.Default"/> for
    /// <see cref="DuplexMode.PrinterDefault"/> (meaning: change nothing).
    /// </returns>
    internal static Duplex ToSystemDuplex(DuplexMode mode) => mode switch
    {
        DuplexMode.None => Duplex.Simplex,

        // "Long edge" binding turns pages like a book, which Windows calls Vertical duplex;
        // "short edge" turns them like a notepad, which is Horizontal.
        DuplexMode.LongEdge => Duplex.Vertical,
        DuplexMode.ShortEdge => Duplex.Horizontal,
        _ => Duplex.Default,
    };

    /// <summary>Copies the options onto an opened printer's settings.</summary>
    private static PrintStatus Apply(PrintOptions options, PrinterSettings settings, string? outputFile, out string message)
    {
        message = Messages.Ok();

        settings.Copies = (short)options.Copies;
        settings.DefaultPageSettings.Color = options.Color;

        // PrinterDefault changes nothing. Plain single-sided printing needs no support from the
        // printer, so it is applied only where it can matter; a double-sided request that the
        // printer cannot do fails here rather than quietly printing single-sided pages.
        if (options.Duplex is DuplexMode.LongEdge or DuplexMode.ShortEdge)
        {
            if (!settings.CanDuplex)
            {
                message = Messages.PrinterUnavailable();
                return PrintStatus.PrinterUnavailable;
            }

            settings.Duplex = ToSystemDuplex(options.Duplex);
        }
        else if (options.Duplex == DuplexMode.None && settings.CanDuplex)
        {
            settings.Duplex = Duplex.Simplex;
        }

        if (!string.IsNullOrWhiteSpace(options.PaperSource))
        {
            PaperSource? source = FindPaperSource(settings, options.PaperSource.Trim());
            if (source is null)
            {
                var available = new List<string>();
                foreach (PaperSource candidate in settings.PaperSources)
                {
                    available.Add(candidate.SourceName);
                }

                message = Messages.UnknownPaperSource(options.PaperSource.Trim(), available);
                return PrintStatus.InvalidOptions;
            }

            settings.DefaultPageSettings.PaperSource = source;
        }

        if (outputFile is not null)
        {
            // Print to file is what stops "Microsoft Print to PDF" from asking where to save.
            settings.PrintToFile = true;
            settings.PrintFileName = outputFile;
        }

        return PrintStatus.Ok;
    }

    /// <summary>Finds a paper source by name, ignoring letter case.</summary>
    private static PaperSource? FindPaperSource(PrinterSettings settings, string name)
    {
        foreach (PaperSource source in settings.PaperSources)
        {
            if (string.Equals(source.SourceName, name, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// Turns the output file option into a full path, or returns <see langword="null"/> when it
    /// cannot be used. A relative path is anchored now because a Windows service's working folder
    /// is not where anyone expects. The folder is not created: making folders as a side effect of
    /// a print call would surprise people, and a missing folder is far better reported up front
    /// than as a print driver error halfway through the job.
    /// </summary>
    private static string? ResolveOutputFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(path.Trim());
            string? folder = Path.GetDirectoryName(full);
            string fileName = Path.GetFileName(full);

            // GetFullPath does not reject characters Windows forbids in a file name (such as | and ?),
            // so check them here; the printer driver would otherwise fail on them mid-job.
            bool usable = folder is not null
                && fileName.Length > 0
                && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && Directory.Exists(folder)
                && !Directory.Exists(full);
            return usable ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
