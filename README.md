<div align="center">

# PDFStream.NET

**Free, open-source PDF printing for .NET.**
Send a PDF to any Windows printer in one line of code. No Adobe Reader, no licence keys, no watermarks.

[![NuGet](https://img.shields.io/nuget/vpre/PDFStream.NET?label=NuGet)](https://www.nuget.org/packages/PDFStream.NET)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D4)
![.NET 8 and 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)
![Licence: MIT](https://img.shields.io/badge/licence-MIT-brightgreen)

</div>

---

> **Status: a release candidate, on NuGet as a prerelease.** Version 1.0.0-rc.1, the first release, was
> published on [nuget.org](https://www.nuget.org/packages/PDFStream.NET) on 2026-11-21; 1.0.0-rc.2 is the version
> with the new package page (icon, package readme, description, tags), with no library code change. The library is
> complete and its automated tests pass, including real prints to *Microsoft Print to PDF* in 64-bit and 32-bit
> processes. The manual release checklist (physical printers, a Windows service, a scheduled task, Arm64
> hardware) is **not complete yet**. The stable 1.0.0 follows once the remaining checklist rows (Arm64, a
> scheduled task, IIS, other physical and duplex printers) are verified. PDF to image conversion is planned after
> 1.0.
> See [what has been verified](#what-has-been-verified).

## Why PDFStream.NET?

Printing a PDF from a .NET application should not need a paid licence, a licence key that expires, or a copy
of Adobe Reader installed on every server. Yet today most developers either pay for a commercial library or
fight with the Windows print system themselves.

PDFStream.NET is a small, focused library that does one job well: **take a PDF and print it, silently, on the
printer you choose.** It is built by [P4 Software](https://github.com/P4Software) because we needed it for our
own warehouse software, and we are sharing it so nobody else has to pay for it.

## What it does

- **Silent printing**: no dialogs, no user interaction, for programs that print with nobody at the screen.
- **Any printer Windows can see**: physical printers and virtual ones such as *Microsoft Print to PDF*.
- **Any input**: a file path, a stream or a byte array.
- **Print options**: page ranges, copies, duplex, colour or black and white, fit to page or actual size,
  paper source, and printing to a file.
- **Password-protected PDFs.**
- **Plain-language results**: every failure comes back as a status and a message that says what went wrong
  and what to do next. It never throws managed exceptions for bad input or printer problems, such as a bad file
  or a paused printer. A crash inside the native PDF engine is the one thing no .NET library can turn into a
  result; see [Security and limits](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md#security-and-limits).
- **No Adobe, nothing else to install**: the PDF engine (PDFium) comes with the package for x64, x86 and Arm64.
- **No licence keys, watermarks, expiry dates, telemetry or network calls. Ever.**

## Install

PDFStream.NET is on [nuget.org](https://www.nuget.org/packages/PDFStream.NET) as a prerelease: 1.0.0-rc.1 was
published on 2026-11-21, and 1.0.0-rc.2 is the version with the new package page. Until the stable 1.0.0 exists, a
plain `dotnet add package PDFStream.NET` finds no stable version, so add the prerelease explicitly:

```text
dotnet add package PDFStream.NET --prerelease
```

or name the version: `dotnet add package PDFStream.NET --version 1.0.0-rc.2`. In the Visual Studio package manager,
tick "Include prerelease" to see it.

The package, the assembly and the namespace are all called `PDFStream.NET`: in your code you write
`using PDFStream.NET;`.

To build the package yourself from this repository, see [CONTRIBUTING.md](https://github.com/P4Software/PDFStream.NET/blob/main/CONTRIBUTING.md).

Requirements:

- Windows 10 or Windows Server 2016, or newer.
- A project that targets a Windows framework: `net8.0-windows`, `net10.0-windows` or newer. (Plain `net8.0`
  cannot reference it, because printing goes through the Windows print system.)

The package brings the PDF engine's `pdfium.dll` for 64-bit, 32-bit and Arm64 Windows, and your build puts the
right one next to your program. There is nothing else to install.

## Use it

```csharp
using PDFStream.NET;

// Print a PDF that is already in memory, for example one just downloaded from an API.
byte[] pdf = await File.ReadAllBytesAsync("packing-slip.pdf");

PrintResult result = PdfPrinter.Print(pdf, new PrintOptions
{
    PrinterName = "Warehouse Printer 1",   // exactly as Windows lists it, or \\server\printer
    Copies = 2,
});

if (!result.Succeeded)
{
    // The message is written for people: log it, or show it to the person running the program. An
    // UnknownError message can end with technical details, so keep it out of public web pages.
    Console.WriteLine(result.Message);
}
```

The same call accepts a file path or a stream, and an optional `CancellationToken`:

```csharp
PrintResult fromFile = PdfPrinter.Print(@"C:\Slips\slip-1042.pdf", new PrintOptions
{
    PrinterName = "Warehouse Printer 1",
    Pages = "1-3, 5",
    Duplex = DuplexMode.LongEdge,
    Color = false,
});

using FileStream stream = File.OpenRead("invoice.pdf");
PrintResult fromStream = PdfPrinter.Print(stream, new PrintOptions
{
    PrinterName = "Microsoft Print to PDF",
    OutputFile = @"C:\Out\invoice-copy.pdf",   // print to a file, no Save As dialog
}, cancellationToken);
```

`result.Status` tells your code what happened (`Ok`, `FileNotFound`, `PasswordRequired`, `PrinterNotFound`,
`PrinterUnavailable`, ...), `result.Message` tells a person, and `result.PagesPrinted` says how many pages
were sent.

## Documentation

- [Usage guide](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md): every option, every status and its message, copies, passwords, printing
  to a file, cancelling, threads.
- [Services, scheduled tasks and IIS](https://github.com/P4Software/PDFStream.NET/blob/main/docs/services-and-iis.md): the account must be able to see the
  printer, and other things a program without a screen needs.
- [Troubleshooting](https://github.com/P4Software/PDFStream.NET/blob/main/docs/troubleshooting.md), organised by the message you got.
- [Contributing](https://github.com/P4Software/PDFStream.NET/blob/main/CONTRIBUTING.md): building, testing and the release checklist.
- [Security](https://github.com/P4Software/PDFStream.NET/blob/main/SECURITY.md): how to report a vulnerability privately; read
  [Security and limits](https://github.com/P4Software/PDFStream.NET/blob/main/docs/usage.md#security-and-limits) before printing PDFs you do not trust.

## Try it without writing code

`samples/PDFStream.NET.TestPrint` is a small program you run by double-clicking `PDFStream.NET.TestPrint.exe`. On the
first run it creates a commented `settings.json` next to itself and lists your printers. Fill in the printer
name and a PDF, and run it again: by default it is a **dry run** that only checks your settings and shows what
it would send. Set `ReallyPrint` to `true` to print. Paths copied with Explorer's "Copy as path" work even
though they carry quote marks. It is also a short example of using the public API.

Every CI build keeps a ready-to-run copy (the `PDFStream.NET.TestPrint-win-x64` artifact): a single exe for 64-bit
(x64) Windows only, which includes .NET and `pdfium.dll`, so .NET does not have to be installed. It is not signed,
so Windows SmartScreen may warn before it runs, and on its first run it extracts `pdfium.dll` to a folder under
your temporary folder. `LICENSE` and `THIRD-PARTY-NOTICES.md` come next to the exe: it carries the .NET runtime
(MIT; its own third-party notices apply too) and PDFium, and any program that redistributes `pdfium.dll` must
carry the PDFium section of those notices. Built from source without those publish options, the sample needs
the .NET 10 runtime.

## What has been verified

On one Windows 11 x64 machine:

- The full test suite for `net8.0-windows` and `net10.0-windows`, in a 64-bit and in a 32-bit (x86) process,
  including real prints to *Microsoft Print to PDF* whose output is checked page by page for ink. Last run on
  2026-11-21, after the name was set to PDFStream.NET: all tests passed, none skipped (452 on `net8.0-windows`,
  477 on `net10.0-windows`).
- The package, from a separate app outside this repository. Under the package's earlier names, on 2026-11-20 and
  2026-11-21: built and published for win-x64, win-x86 and win-arm64 on both frameworks, the right `pdfium.dll`
  lands next to the app, and the x64 and x86 builds printed a two-page PDF to *Microsoft Print to PDF* (the
  CHANGELOG lists each run and the spelling of the name at the time). Under its final name PDFStream.NET, on
  2026-11-21: the package was packed as `PDFStream.NET.1.0.0-rc.1.nupkg` and used from a separate win-x64 app on
  `net10.0-windows` with `using PDFStream.NET;`, which printed a PDF to *Microsoft Print to PDF*, and the right
  `pdfium.dll` landed next to the exe.
- Version 1.0.0-rc.2 (the package page; the library code is unchanged), on 2026-11-21, in a 64-bit process only:
  the full test suite passed on both frameworks (452 on `net8.0-windows`, 477 on `net10.0-windows`, none
  skipped); `PDFStream.NET.1.0.0-rc.2.nupkg` passed the package check with the icon and the package readme
  inside; and a separate win-x64 app on `net10.0-windows` restored it into an empty package cache and printed a
  one-page PDF to *Microsoft Print to PDF* with `OutputFile` (status Ok, one page in the output file).

Not verified yet (they are on the [release checklist](https://github.com/P4Software/PDFStream.NET/blob/main/docs/release-checklist.md)): running on an Arm64
PC, physical printers (duplex, network printers), and printing from a Windows service, a scheduled task
or IIS. One real print through an installed print service on one PC has been reported as working; that is a
single report, so the checklist row for Windows services stays open.

## Roadmap

| Step | What | Status |
|------|------|--------|
| 1 | Project set-up, licence, build pipeline | Done |
| 2 | Silent printing of a PDF to a chosen printer | Done |
| 3 | Print options (ranges, copies, duplex, colour, scaling, paper source, print to file) | Done |
| 4 | Password-protected PDFs and clear error messages | Done |
| 5 | First NuGet release (1.0.0-rc.1, published 2026-11-21) | Done |
| 6 | A proper package page (1.0.0-rc.2), the manual release checklist, then the stable 1.0.0 | In progress |
| 7 | PDF to image conversion (PNG, JPG, TIFF), planned after 1.0 | Planned |

## Get involved

PDFStream.NET is open source and built in the open. Bug reports, printer and driver reports and pull requests
are welcome: open an [issue](https://github.com/P4Software/PDFStream.NET/issues), especially about the printers,
servers and PDF files that give you trouble today. See [CONTRIBUTING.md](https://github.com/P4Software/PDFStream.NET/blob/main/CONTRIBUTING.md).

## Licence

PDFStream.NET is free and open source under the [MIT License](https://github.com/P4Software/PDFStream.NET/blob/main/LICENSE), copyright 2026 P4 Software. Use it in
personal, commercial and closed-source projects, with no fees and no licence keys. The PDF engine and the
libraries inside it keep their own permissive licences; see
[THIRD-PARTY-NOTICES.md](https://github.com/P4Software/PDFStream.NET/blob/main/THIRD-PARTY-NOTICES.md).

---

<div align="center">

Made by [P4 Software](https://github.com/P4Software)

</div>
