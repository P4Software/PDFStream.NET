# Changelog

All notable changes to PDFStream are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

Version 1.0 release candidate. Not published on NuGet yet; the manual release checklist is not complete.

### Added

- Solution scaffold: the `PDFStream` library and its xUnit.net test project, targeting
  `net8.0-windows` and `net10.0-windows`.
- `PdfPrinter.Print` for a file path, a stream or a byte array, with `PrintOptions` (printer name, document
  name, password, page range, copies, duplex, colour, fit to page or actual size, paper source, output file)
  and an optional `CancellationToken`. It never throws for bad input or printer problems: it returns a
  `PrintResult` with a `PrintStatus`, a plain-language message that says what happened and what to do next,
  and the number of pages sent.
- Page ranges such as `1-5, 8, 10-12`, printed in the order written.
- Printer lookup by the exact name Windows lists (network names included), and a check of the printer's queue
  before printing: a paused queue, "Use printer offline", a printer that is not available or being deleted,
  and a queue that cannot be opened (a stopped Print Spooler included) are refused with a message naming the
  cause. A printer that only reports Offline is not refused.
- Filled-in form fields (text fields, checkboxes and the other fields of an ordinary PDF form) print on both
  print paths. PDFium never draws form fields when it draws a page, so before a page with fields is first
  drawn PDFStream copies that page inside its own in-memory copy of the PDF, with everything the page inherits
  from the PDF's page tree (its fonts, images and page boxes included), flattens the fields into the copy as
  they print, and prints the copy in place of the page. Everything else on the page prints as before; fields
  not marked for printing stay off the paper, and pages without fields are untouched. XFA-only forms are not
  supported (the bundled PDFium is built without XFA).
- Printing straight onto the printer's page as drawing commands, with a picture fallback (at most 300 dpi)
  for drivers that refuse that; each page is scaled and turned on its own. Copies are made by the driver when
  it can, otherwise by sending the document again in the same job.
- Internal PDF engine behind an `IPdfEngine` / `IPdfDocument` seam, implemented with PDFium:
  loads a PDF from memory; tells "no password given" apart from "wrong password"; opens PDFs that
  only have an owner password without asking for one; reports page sizes in points; renders a page
  straight onto a Windows device context (with PDFium's printing flag) or onto a white bitmap at any
  resolution.
- One process-wide lock around every PDFium call, because PDFium is not thread-safe. Calls from
  several threads are safe; the lock is held per engine call, so concurrent jobs interleave at page level.
- PDFium 160.0.8165 native binaries for win-x64, win-x86 and win-arm64 (package
  `bblanchon.PDFium.Win32`), with their licences in `THIRD-PARTY-NOTICES.md`.
- Real-print acceptance tests through *Microsoft Print to PDF* that check the produced pages for ink, and a
  manual release checklist (`docs/release-checklist.md`).
- `samples/PDFStream.TestPrint`: a double-click test-print program (net10.0-windows). It reads a
  commented `settings.json` next to the exe (created on the first run), lists the installed printers,
  and does a dry run by default; `ReallyPrint` set to `true` really prints. It always waits for a key
  press so the result can be read, uses only the public API, and accepts paths pasted with quote marks.
- NuGet package metadata: the README as the package page, the MIT licence expression, the repository and
  project address, tags, and `LICENSE` and `THIRD-PARTY-NOTICES.md` inside the package.
- Documentation: `docs/usage.md`, `docs/services-and-iis.md`, `docs/troubleshooting.md` and `CONTRIBUTING.md`.
- CI (`.github/workflows/ci.yml`): on every push and pull request, build and test both frameworks on
  `windows-latest`, report whether *Microsoft Print to PDF* exists and how many tests were skipped, pack and
  check the NuGet package, and publish the test-print sample as one self-contained exe, with `LICENSE` and
  `THIRD-PARTY-NOTICES.md` next to it (the exe carries the .NET runtime and `pdfium.dll`). Artifacts only; it
  publishes nothing and uses no secrets.

### Changed

- The C# language version is pinned to 12.0 instead of "latest", so the language no longer changes with the
  installed SDK. C# 12 is the newest version the .NET 8 SDK supports.

### Verified

Test hosts actually run for this change (2026-11-20, one Windows 11 x64 machine, .NET SDK 11.0.100):

- win-x64: `net8.0-windows` (288 tests) and `net10.0-windows` (313 tests), all passed, none skipped.
- win-x86: `net8.0-windows` and `net10.0-windows`
  (`dotnet test --project tests/PDFStream.Tests/PDFStream.Tests.csproj --arch x86`), all passed;
  the tests logged `Process architecture: X86`.
- win-arm64: **not run**. No arm64 machine was available, and an x64 machine cannot execute arm64
  code. The arm64 `pdfium.dll` is present in the package and exports the same functions, but
  loading it is unverified.
- The NuGet package from a separate consumer app outside the repository, restored from a local folder
  into an empty package cache, with the .NET 10 SDK (10.0.404) on `net8.0-windows` and `net10.0-windows` and
  the .NET 8 SDK (8.0.428) on `net8.0-windows`: for win-x64, win-x86 and win-arm64, both `dotnet build -r`
  and `dotnet publish -r` put the `pdfium.dll` of that architecture next to the exe (checked by SHA-256
  against the PDFium package and by the file's machine type); a build with no runtime identifier puts all
  three under `runtimes`. The x64, x86 and no-runtime-identifier builds were run and printed a two-page PDF
  to *Microsoft Print to PDF* (two pages in the output file). The arm64 builds were checked as files only.
- The self-contained single-file test-print sample (win-x64) printed a two-page PDF to
  *Microsoft Print to PDF*.
