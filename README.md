<div align="center">

# PDFStream

**Free, open-source PDF printing for .NET.**
Send a PDF to any Windows printer in one line of code. No Adobe Reader, no licence keys, no watermarks.

![Status: early development](https://img.shields.io/badge/status-early%20development-orange)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Open source](https://img.shields.io/badge/open%20source-free%20forever-brightgreen)

</div>

---

> **Heads up: PDFStream is just getting started.**
> The code is not published yet. This page describes what we are building and why,
> so you can follow along, give feedback and help shape it.

## Why PDFStream?

Printing a PDF from a .NET application should not need a paid licence, a licence key that
expires, or a copy of Adobe Reader installed on every server. Yet today most developers either
pay for a commercial library or fight with the Windows print system themselves.

PDFStream is a small, focused library that does one job well: **take a PDF and print it, silently,
on the printer you choose.** It is built by [P4 Software](https://github.com/P4Software) because we
needed it for our own warehouse software, and we are sharing it so nobody else has to pay for it.

## What it will do (version 1)

- **Silent printing**: no dialogs, no user interaction. Works from services, scheduled tasks and servers.
- **Any printer Windows can see**: physical printers and virtual ones such as *Microsoft Print to PDF*.
- **Any input**: a file path, a stream or a byte array.
- **Print options**: page ranges, number of copies, duplex, colour or black and white, scaling, paper source.
- **Password-protected PDFs**.
- **Plain-language results**: every failure comes back as a clear message that says what went wrong
  and what to do next, not a cryptic code.
- **No Adobe, no third-party installs**: the PDF rendering engine ships inside the package.
- **No licence keys. Ever.**

Coming after version 1: PDF to image conversion (PNG, JPG, TIFF).

## What it will look like

This is a design sketch of the planned API, not final code:

```csharp
// Print a PDF that is already in memory, for example one just downloaded from an API.
byte[] pdf = await File.ReadAllBytesAsync("packing-slip.pdf");

PrintResult result = PdfPrinter.Print(pdf, new PrintOptions
{
    PrinterName = "Warehouse Printer 1",
    Copies = 2,
});

if (!result.Succeeded)
{
    // The message is written for people, so it can go straight into a log or a screen.
    Console.WriteLine(result.Message);
}
```

## Requirements

- Windows 10 / Windows Server 2016 or newer
- .NET 10 (a Windows target such as `net10.0-windows`)

Linux and macOS are not planned for version 1 because printing goes through the Windows print system.

## Roadmap

| Step | What | Status |
|------|------|--------|
| 1 | Project set-up, licence, build pipeline | Planned |
| 2 | Silent printing of a PDF to a chosen printer | Planned |
| 3 | Print options (ranges, copies, duplex, colour) | Planned |
| 4 | Password-protected PDFs and clear error messages | Planned |
| 5 | First NuGet release | Planned |
| 6 | PDF to image conversion | Planned |

## Get involved

PDFStream is open source and built in the open. Ideas, bug reports and pull requests will be
welcome as soon as the first code lands. For now, the best way to help is to open an
[issue](https://github.com/P4Software/PDFStream/issues) and tell us what you would use it for,
especially the printers, servers and PDF files that give you trouble today.

## Licence

PDFStream is free and open source. The exact licence will be added with the first code release.

---

<div align="center">

Made by [P4 Software](https://github.com/P4Software)

</div>
