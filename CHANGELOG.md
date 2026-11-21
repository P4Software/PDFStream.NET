# Changelog

All notable changes to PDFStream are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Solution scaffold: the `PDFStream` library and its xUnit.net test project, targeting
  `net8.0-windows` and `net10.0-windows`.
- Internal PDF engine behind an `IPdfEngine` / `IPdfDocument` seam, implemented with PDFium:
  loads a PDF from memory; tells "no password given" apart from "wrong password"; opens PDFs that
  only have an owner password without asking for one; reports page sizes in points; renders a page
  straight onto a Windows device context (with PDFium's printing flag) or onto a white bitmap at any
  resolution.
- One process-wide lock around every PDFium call, because PDFium is not thread-safe. Calls from
  several threads are safe and take turns.
- PDFium 160.0.8165 native binaries for win-x64, win-x86 and win-arm64 (package
  `bblanchon.PDFium.Win32`), with their licences in `THIRD-PARTY-NOTICES.md`.

- `samples/PDFStream.TestPrint`: a double-click test-print program (net10.0-windows). It reads a
  commented `settings.json` next to the exe (created on the first run), lists the installed printers,
  and does a dry run by default; `ReallyPrint` set to `true` really prints. It always waits for a key
  press so the result can be read, and uses only the public API.

### Verified

Test hosts actually run for this change (2026-11-20, one Windows 11 x64 machine, .NET SDK 11.0.100):

- win-x64: `net8.0-windows` and `net10.0-windows`, all tests passed.
- win-x86: `net8.0-windows` and `net10.0-windows`
  (`dotnet test --project tests/PDFStream.Tests/PDFStream.Tests.csproj --arch x86`), all tests passed;
  the tests logged `Process architecture: X86`.
- win-arm64: **not run**. No arm64 machine was available, and an x64 machine cannot execute arm64
  code. The arm64 `pdfium.dll` is present in the package and exports the same functions, but
  loading it is unverified.
