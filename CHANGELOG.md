# Changelog

All notable changes to PDFStream.NET are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## 1.0.0-rc.2 - 2026-11-21

A package-page release. A version on nuget.org can never be edited, so the new nuget.org page ships as a new
version. There is no library code change: the assemblies are built from the same code as 1.0.0-rc.1, and the
manual release checklist rows are as open as they were for 1.0.0-rc.1.

### Added

- A package icon, `src/PDFStream.NET/icon.png` (256 x 256, flat P4 Software brand colours): a PDF page with a
  folded orange corner whose downward "stream" chevrons lead into a printer, with the printed sheet coming out.
  It is drawn by `tools/make-icon.ps1` (PowerShell 7 and System.Drawing, no other tools), so it can be rebuilt
  and reviewed like code; running the script twice gives the same file.
- A package readme written for nuget.org, `src/PDFStream.NET/README.nuget.md`, packed as `README.md`: a one-line
  pitch, badges, why, features, a quick start, a stream, page range and duplex example, install, requirements, a
  status box with what is and is not verified, security and limits, and links. It uses no HTML, only absolute
  https links, and images only from shields.io. The repository `README.md` stays the GitHub page and is no longer
  packed.
- `.github/scripts/check-package.ps1`: the package check, moved out of `ci.yml` into one script, the only place
  that pins the version being released.

### Changed

- Package metadata: a benefit-led description, search tags (`pdf print printing printer silent windows pdfium
  batch automation server service pdf-printing`), release notes with a link to this changelog, and the icon.
- The package check now also fails without `icon.png` at the package root or `<icon>icon.png</icon>` in the
  nuspec, for an icon that is not a square PNG of at least 128 px and at most 1 MB, without release notes, and
  when `README.md` in the package is not the package readme (raw HTML, a link or image that is not absolute
  https, or no install command for the version being released). It expects version 1.0.0-rc.2.
- `README.md`, `SECURITY.md` and `CONTRIBUTING.md` no longer say the package is unpublished: 1.0.0-rc.1 was
  published on nuget.org on 2026-11-21. `README.md` has a NuGet badge, and `CONTRIBUTING.md` says where the
  package readme, the icon and the version pin live.
- `docs/release-checklist.md` records that 1.0.0-rc.1 was published with the manual rows still Not run, and one
  reported (not signed-off) print through an installed print service.

### Verified

On one Windows 11 x64 machine, .NET SDK 11.0.100, on 2026-11-21:

- `dotnet build -warnaserror` from the repository root: 0 warnings, 0 errors, on `net8.0-windows` and
  `net10.0-windows`.
- The full test suite in a 64-bit process: `net8.0-windows` 452 passed and `net10.0-windows` 477 passed, none
  skipped (Debug). The 32-bit runs were not repeated for this version, which has no library code change.
- `dotnet pack -c Release` into a folder outside the repository gave `PDFStream.NET.1.0.0-rc.2.nupkg` with
  `icon.png`, `README.md` (byte for byte `README.nuget.md`), `LICENSE`, `THIRD-PARTY-NOTICES.md` and
  `lib/net8.0-windows7.0/PDFStream.NET.dll` and `lib/net10.0-windows7.0/PDFStream.NET.dll`; the nuspec has the
  icon, the readme, the tags and the release notes. The package check passed against it, and failed, as it must,
  against copies without `icon.png`, without `<icon>`, without `README.md` and with the GitHub README in its place.
- The two C# samples in `README.nuget.md`, compiled unchanged as console programs against the library for both
  frameworks with warnings as errors: 0 warnings, 0 errors.
- A separate win-x64 app on `net10.0-windows` restored the package (`<PackageReference Include="PDFStream.NET"
  Version="1.0.0-rc.2" />`) from that folder into an empty package cache and printed a one-page PDF to
  *Microsoft Print to PDF* with `OutputFile` (status Ok, one page sent, a one-page PDF written), and the
  `pdfium.dll` next to its exe was the x64 one from the PDFium package (same SHA-256).
- `README.nuget.md` rendered through GitHub's markdown API: one table, two block quotes, all links and images
  absolute https. nuget.org renders with its own engine, which was not seen before publishing.
- Against nuget.org: a plain `dotnet add package PDFStream.NET` stops with "There are no stable versions
  available, 1.0.0-rc.1 is the best available. Consider adding the --prerelease option", and
  `dotnet add package PDFStream.NET --prerelease` adds 1.0.0-rc.1.

## 1.0.0-rc.1 - 2026-11-21

The first release of PDFStream.NET, a release candidate (a prerelease on NuGet). The stable 1.0.0 follows once the
remaining manual release checklist rows are verified (Arm64, a scheduled task, IIS, other physical and duplex
printers); see "Verified" below for what was run and what was not.

### Added

- Solution scaffold: the `PDFStream.NET` library and its xUnit.net test project, targeting
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
  print paths. PDFium never draws form fields when it draws a page, so PDFStream.NET copies the pages with fields
  that a print call prints, in one go, inside its own in-memory copy of the PDF, with everything each page
  inherits from the PDF's page tree (its fonts, images and page boxes included), flattens the fields into the
  copies as they print, and prints the copies in place of the pages. What the pages share is copied once, and
  the copies are made only for a PDF of at most 64 MB (the copy budget). A copy is used only if it carries all
  of the page's annotations and looks exactly like the page; otherwise (for example on a page with layers the
  PDF switches off, a page with a damaged reference, or a PDF over the budget) the fields are drawn into the
  page itself, and they still print. Everything else on the page prints as before; fields not marked for
  printing stay off the paper, and pages without fields are untouched. Known limitation: a page with fields
  that takes fonts or images from the PDF's page tree, and whose copy cannot be used, prints without those
  fonts or images. Copying relies on `FPDF_ImportPagesByIndex`, which PDFium marks as experimental, tested only
  with the bundled PDFium 160.0.8165. XFA-only forms are not supported (the bundled PDFium is built without
  XFA).
- Printing straight onto the printer's page as drawing commands, with a picture fallback (at most 300 dpi)
  for drivers that refuse that; each page is scaled and turned on its own. Copies are made by the driver when
  it can, otherwise by sending the document again in the same job.
- Internal PDF engine behind an `IPdfEngine` / `IPdfDocument` seam, implemented with PDFium:
  loads a PDF from memory; tells "no password given" apart from "wrong password"; opens PDFs that
  only have an owner password without asking for one; reports page sizes in points; renders a page
  straight onto a Windows device context (with PDFium's printing flag) or onto a white bitmap at any
  resolution.
- One process-wide lock around every PDFium call, because PDFium is not thread-safe. Calls from
  several threads are safe; the lock is held per engine call, so concurrent jobs interleave at page level (the first form page of a document is a longer call; see Known limitations).
- PDFium 160.0.8165 native binaries for win-x64, win-x86 and win-arm64 (package
  `bblanchon.PDFium.Win32`), with their licences in `THIRD-PARTY-NOTICES.md`.
- Real-print acceptance tests through *Microsoft Print to PDF* that check the produced pages for ink, and a
  manual release checklist (`docs/release-checklist.md`).
- `samples/PDFStream.NET.TestPrint`: a double-click test-print program (net10.0-windows). It reads a
  commented `settings.json` next to the exe (created on the first run), lists the installed printers,
  and does a dry run by default; `ReallyPrint` set to `true` really prints. It always waits for a key
  press so the result can be read, uses only the public API, and accepts paths pasted with quote marks.
- NuGet package metadata: the package id and title `PDFStream.NET`, the README as the package page, the MIT
  licence expression, the repository and project address, tags, and `LICENSE` and `THIRD-PARTY-NOTICES.md`
  inside the package.
- Documentation: `docs/usage.md`, `docs/services-and-iis.md`, `docs/troubleshooting.md` and `CONTRIBUTING.md`.
- CI (`.github/workflows/ci.yml`): on every push and pull request, build and test both frameworks on
  `windows-latest`, report whether *Microsoft Print to PDF* exists and how many tests were skipped, pack and
  check the NuGet package, and publish the test-print sample as one self-contained exe, with `LICENSE` and
  `THIRD-PARTY-NOTICES.md` next to it (the exe carries the .NET runtime and `pdfium.dll`). Artifacts only; it
  publishes nothing and uses no secrets.

### Changed

- The product is named `PDFStream.NET` everywhere: the NuGet package (`dotnet add package PDFStream.NET`), the
  assembly (`PDFStream.NET.dll`) and the C# namespace, so code says `using PDFStream.NET;`. The projects, the
  solution and the test-print sample carry the same name. The type names (`PdfPrinter`, `PrintOptions`, ...) are
  unchanged. This was decided before the first release, while no program depends on the library yet.
- The name is written PDFStream.NET, with a capital NET (like SheetStream.NET), in the package id and title, the
  assembly, the namespace, the folders and project files, the repository address
  (https://github.com/P4Software/PDFStream.NET) and all the documentation. NuGet keeps the capitalisation of the
  first publish permanently, so it is fixed before the first release. Earlier drafts wrote it `PDFStream.net`.

- The C# language version is pinned to 12.0 instead of "latest", so the language no longer changes with the
  installed SDK. C# 12 is the newest version the .NET 8 SDK supports.

### Known limitations

- Form fields: in a PDF with three or more form pages that share one indirect `/Contents` array (and one
  indirect `/Resources` dictionary), later pages can also print the fields of earlier pages, with status `Ok`.
  The check of each page copy catches the first affected page, but the fallback that draws the fields into the
  page itself edits the shared array again. Workaround: flatten such a PDF before printing, or print its pages as
  separate documents. A characterization test pins this behaviour; fixing it means changing that test and the
  documentation. Planned for 1.0.
- Form fields: a crafted PDF with inline inherited values can make form printing use a lot of memory (about
  600 MB measured for a 1.2 MB PDF with 100 form pages); print untrusted PDFs in a separate worker process.
- The first page with form fields of a document runs one longer engine call that copies every form page the call
  prints (about 77 ms on x64 and 95 ms on x86 for 500 form pages, as measured in a review); it holds the
  process-wide lock and cannot be cancelled.
- If reloading a flattened page fails, which is practically unreachable, the page is drawn with blank form
  fields and status `Ok`.

### Verified

Test hosts actually run for this change (one Windows 11 x64 machine, .NET SDK 11.0.100; the test counts are
from the last run, on 2026-11-21, Debug build, after the name was set to PDFStream.NET):

- win-x64: `net8.0-windows` (452 tests) and `net10.0-windows` (477 tests), all passed, none skipped.
- win-x86: `net8.0-windows` (452 tests) and `net10.0-windows` (477 tests)
  (`dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --arch x86`), all passed, none
  skipped; the form, engine, real-print and memory tests logged `Process architecture: X86`. On an earlier
  32-bit `net10.0-windows` run that day, one real-print test timed out after 30 seconds waiting for
  *Microsoft Print to PDF* to finish writing its file, and the re-run passed; that wait is now 120 seconds, and
  a timeout reports the state of the print queue.
- win-arm64: **not run**. No arm64 machine was available, and an x64 machine cannot execute arm64
  code. The arm64 `pdfium.dll` is present in the package and exports the same functions, but
  loading it is unverified.
- On 2026-11-20, under the package's earlier id (before the name PDFStream.net was chosen): the NuGet package
  from a separate consumer app outside the repository, restored from a local folder
  into an empty package cache, with the .NET 10 SDK (10.0.404) on `net8.0-windows` and `net10.0-windows` and
  the .NET 8 SDK (8.0.428) on `net8.0-windows`: for win-x64, win-x86 and win-arm64, both `dotnet build -r`
  and `dotnet publish -r` put the `pdfium.dll` of that architecture next to the exe (checked by SHA-256
  against the PDFium package and by the file's machine type); a build with no runtime identifier puts all
  three under `runtimes`. The x64, x86 and no-runtime-identifier builds were run and printed a two-page PDF
  to *Microsoft Print to PDF* (two pages in the output file). The arm64 builds were checked as files only.
- On 2026-11-21, under the spelling `PDFStream.net` (lowercase, before the final spelling PDFStream.NET was chosen):
  the package was packed again and
  used from separate win-x64 and win-x86 apps outside the repository (empty package cache, local folder feed,
  `using PDFStream.net;`); both printed a two-page PDF to *Microsoft Print to PDF* (two pages in the output file) and
  `pdfium.dll` of the right architecture was next to the exe. The package holds `lib/net8.0-windows7.0/PDFStream.net.dll`
  and `lib/net10.0-windows7.0/PDFStream.net.dll`; the self-contained test-print sample was published as
  `PDFStream.net.TestPrint.exe` and its dry run says `PDFStream.net test print`.
- On 2026-11-21, still under the lowercase spelling, after the form-printing changes of that day: `dotnet pack -c Release` into a folder outside the
  repository gave `PDFStream.net.1.0.0-preview.nupkg`, and the package check from `ci.yml` passed against it. A
  separate win-x64 app on `net10.0-windows` restored it from that folder into an empty package cache, reported
  the assembly and the namespace as `PDFStream.net`, and printed a one-page PDF with a filled form field to
  *Microsoft Print to PDF* (status Ok, one page, output file written; its content was not inspected).
- On 2026-11-20: the self-contained single-file test-print sample (win-x64) printed a two-page PDF to
  *Microsoft Print to PDF*.
- On 2026-11-21, still under the lowercase spelling, with the version set to 1.0.0-rc.1: `dotnet pack -c Release` into a folder outside the repository
  gave `PDFStream.net.1.0.0-rc.1.nupkg` (lowercase spelling; nuspec version `1.0.0-rc.1`; `AssemblyVersion` and `FileVersion` 1.0.0.0; the
  informational version is `1.0.0-rc.1` plus the commit hash), and the package check from `ci.yml` passed against it.
  A separate win-x64 app on `net10.0-windows` restored it (`<PackageReference Include="PDFStream.net" Version="1.0.0-rc.1" />`)
  from that folder into an empty package cache and printed a two-page PDF to *Microsoft Print to PDF* (status Ok,
  two pages, output file written with two pages).
- On 2026-11-21, after the name was set to `PDFStream.NET` (capital NET): `dotnet build -warnaserror` gave 0 warnings
  on both frameworks; `dotnet pack -c Release` into a folder outside the repository gave
  `PDFStream.NET.1.0.0-rc.1.nupkg` (nuspec id and title `PDFStream.NET`, repository
  `https://github.com/P4Software/PDFStream.NET`, `lib/net8.0-windows7.0/PDFStream.NET.dll` and
  `lib/net10.0-windows7.0/PDFStream.NET.dll`), and the package check from `ci.yml` passed against it. The check
  failed, as it must, against a pack with the lowercase id, one with a lowercase assembly name and one with the
  lowercase repository address. A separate win-x64 app on `net10.0-windows` restored it into an empty package cache,
  wrote `using PDFStream.NET;`, printed a one-page PDF to *Microsoft Print to PDF* (status Ok, output file written),
  and `pdfium.dll` of the right architecture was next to the exe. The test-print sample was published the way CI does
  it: `PDFStream.NET.TestPrint.exe` with `LICENSE` and `THIRD-PARTY-NOTICES.md` beside it, and its dry run says
  `PDFStream.NET test print`.
