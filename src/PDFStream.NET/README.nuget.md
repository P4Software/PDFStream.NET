# PDFStream.NET

**Silent PDF printing for .NET on Windows.** Send a PDF to any printer Windows can see in one call: no print
dialog, no Adobe Reader, no licence key.

[![NuGet](https://img.shields.io/nuget/vpre/PDFStream.NET?label=NuGet&color=0A5FAD)](https://www.nuget.org/packages/PDFStream.NET) [![Licence: MIT](https://img.shields.io/badge/licence-MIT-0A5FAD)](https://github.com/P4Software/PDFStream.NET/blob/main/LICENSE) [![Platform: Windows](https://img.shields.io/badge/platform-Windows-0A5FAD)](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md#requirements) [![.NET 8 and 10](https://img.shields.io/badge/.NET-8%20%7C%2010-0A5FAD)](https://dotnet.microsoft.com/download)

> **Release candidate.** The library is complete and its automated tests pass. Some manual release checks are
> still open; the **Status** section below says exactly what has and has not been verified.

## Why PDFStream.NET

- **Free and open source.** MIT licence: use it in personal, commercial and closed-source software, with no fees.
- **Nothing to activate.** No licence keys, watermarks, expiry dates, telemetry or network calls.
- **Nothing else to install.** No Adobe Reader or other PDF viewer: the PDFium engine comes with the package.
- **Made for unattended printing.** Background jobs, Windows services and servers that print with nobody at the
  screen. No dialog ever opens.
- **Plain-language results.** Bad input and printer problems come back as a status and a message that says what
  went wrong and what to do next, not as exceptions.

## Features

| Feature | Details |
| --- | --- |
| **Input** | A file path, a `Stream` or a `byte[]` |
| **Printers** | Any printer Windows lists, local or network (`\\server\printer`), including virtual printers such as *Microsoft Print to PDF* |
| **Options** | Page ranges (`"1-3, 7"`), copies, duplex, colour or black and white, fit to page or actual size, paper source, print to a file |
| **PDFs** | Password-protected PDFs; filled-in form fields (AcroForm) print with their values |
| **Results** | `PrintResult`: a `PrintStatus` to branch on, a message for people, and the number of pages sent |
| **Threads** | Safe to call from several threads at once |
| **Cancelling** | An optional `CancellationToken`, checked between pages |
| **Engine** | PDFium, the open-source engine of Chrome, for x64, x86 and Arm64 Windows |

## Quick start

```csharp
using PDFStream.NET;

PrintResult result = PdfPrinter.Print("invoice.pdf", new PrintOptions
{
    PrinterName = "Office Printer",       // exactly as Windows lists it, or \\server\printer
    Copies = 2,
});

Console.WriteLine(result.Succeeded
    ? $"Sent {result.PagesPrinted} pages to the printer."
    : $"Not printed ({result.Status}): {result.Message}");
```

`result.Status` tells your code what happened (`Ok`, `FileNotFound`, `PasswordRequired`, `PrinterNotFound`,
`PrinterUnavailable`, ...) and `result.Message` tells a person what to do about it.

## Streams, page ranges and duplex

```csharp
using PDFStream.NET;

// A PDF from a stream: an upload, a download or a database column. It is read from its current position.
using FileStream pdf = File.OpenRead("monthly-report.pdf");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

PrintResult result = PdfPrinter.Print(pdf, new PrintOptions
{
    PrinterName = @"\\print-server\Finance",
    DocumentName = "Monthly report",      // the name shown in the print queue
    Pages = "1-3, 7",                     // printed in the order written
    Duplex = DuplexMode.LongEdge,         // both sides, bound like a book
    Color = false,                        // black and white
}, timeout.Token);
```

To print to a file instead of paper, set `PrinterName = "Microsoft Print to PDF"` and
`OutputFile = @"C:\Out\copy.pdf"`: the file is written with no Save As dialog.

## Install

```text
dotnet add package PDFStream.NET --prerelease
```

or name the version:

```text
dotnet add package PDFStream.NET --version 1.0.0-rc.2
```

Until the stable 1.0.0 is released, add `--prerelease` or the version, and tick "Include prerelease" in the
Visual Studio package manager. The package, the assembly and the namespace are all `PDFStream.NET`.

## Requirements

- Windows 10 or Windows Server 2016, or newer.
- A project that targets `net8.0-windows` or `net10.0-windows` (or newer). Plain `net8.0` cannot reference it,
  because printing goes through the Windows print system.
- Nothing else on the machine. The `bblanchon.PDFium.Win32` dependency brings `pdfium.dll` for x64, x86 and
  Arm64, and your build puts the right one next to your program.

## Status

> **Version 1.0.0-rc.2 is a release candidate (a prerelease).** It changes the package page only: the library
> code is the same as in 1.0.0-rc.1.
>
> **Verified:** the full automated test suite on `net8.0-windows` and `net10.0-windows`, in 64-bit and 32-bit
> processes, including real prints to *Microsoft Print to PDF* whose output is checked page by page; the package
> used from a separate app that printed through it. One real print through an installed print service on one PC
> has also been reported as working; that is a single report, so the checklist row for Windows services stays
> open.
>
> **Not verified yet:** Arm64 hardware, a scheduled task, IIS, and other physical and duplex printers. These rows
> of the [release checklist](https://github.com/P4Software/PDFStream.NET/blob/main/docs/release-checklist.md) are still open. The stable 1.0.0 follows
> once they are verified.

## Security and limits

- **PDFium runs inside your process, without a sandbox.** A malformed or hostile PDF that crashes it ends the
  whole process. Print PDFs you do not trust in a separate worker process.
- **One hung printer driver stalls every print in the process**, because one lock guards the PDF engine.
  Cancellation is checked between pages, so it cannot interrupt a page that is being drawn.
- **Form printing has two known limits:** a crafted PDF can make it use a lot of memory, and in PDFs whose form
  pages share one content array, later pages can show earlier pages' fields.

Read [Security and limits](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md#security-and-limits) before printing PDFs from outside
your organisation, and report vulnerabilities privately as described in the
[security policy](https://github.com/P4Software/PDFStream.NET/blob/main/SECURITY.md).

## Documentation

- [Usage guide](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md): every option, every status and its message.
- [Services, scheduled tasks and IIS](https://github.com/P4Software/PDFStream.NET/blob/main/docs/services-and-iis.md): printing with nobody at the screen.
- [Troubleshooting](https://github.com/P4Software/PDFStream.NET/blob/main/docs/troubleshooting.md), organised by the message you got.
- [Changelog](https://github.com/P4Software/PDFStream.NET/blob/main/CHANGELOG.md) and [release checklist](https://github.com/P4Software/PDFStream.NET/blob/main/docs/release-checklist.md).
- [Issues](https://github.com/P4Software/PDFStream.NET/issues): bugs, printer and driver reports, questions.
- [MIT licence](https://github.com/P4Software/PDFStream.NET/blob/main/LICENSE) and [third-party notices](https://github.com/P4Software/PDFStream.NET/blob/main/THIRD-PARTY-NOTICES.md).

Made by [P4 Software](https://github.com/P4Software). Source code: [github.com/P4Software/PDFStream.NET](https://github.com/P4Software/PDFStream.NET).
