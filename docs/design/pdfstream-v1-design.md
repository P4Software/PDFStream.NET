# PDFStream.NET v1: design

Date: 2026-11-20. Status: the design the first release (the release candidate 1.0.0-rc.1, then the stable 1.0.0) is
built to; "v1" in this document and in its file name means that first release. Owner: P4 Software.

## Intent and why

PDFStream.NET is a free, open-source .NET library that prints a PDF, silently, to a Windows printer.
Commercial libraries do this today, but they cost money, often depend on licence keys, or need Adobe
Reader installed. We needed it for our own warehouse software, and decided to build it and give it
away. The first real user is our warehouse software's print service, a Windows service that prints
packing slips and bills of lading to printers at customer sites; every other .NET developer who prints
PDFs from a service or server is the audience.

## Goals

1. **Print a PDF silently** to any printer Windows can see (physical or virtual), from a file path,
   a stream or a byte array. No dialogs, no Adobe, no licence key, no watermark.
2. **Work where servers work**: inside Windows services, scheduled tasks and IIS app pools
   (subject to the printer being visible to that account, which the docs must explain).
3. **Print options**: page ranges, copies, duplex, colour or black and white, scaling to fit,
   paper source.
4. **Password-protected PDFs.**
5. **Failures come back as a result, never an unexpected exception**, with a status and a plain
   language message that says what happened and what to do next.
6. **Serve the first user**: the warehouse print service switches to PDFStream.NET by changing its single
   print call. This is the acceptance test (see below).
7. Ships as the NuGet package `PDFStream.NET` with the PDF engine inside it.

## Non-goals (not in v1)

- PDF viewer or editor components.
- Linux, macOS or any non-Windows printing (printing goes through the Windows print system).
- Printing through Adobe Reader.
- Printing several documents as one print job.
- Output-tray selection.
- A hosted service, a paid edition, licence keys or telemetry.

## Anti-goals (a failure even if it technically works)

- Any watermark, nag, trial period, expiry or licence key.
- Needing Adobe Reader or any other install on the machine.
- A copyleft or non-redistributable dependency (it would bind every user).
- Network calls or data collection of any kind.
- Reporting success when the printout is blank or wrong. "Printed" must mean pages were really
  rendered and sent.

## Constraints

- **Licence: MIT.** All dependencies must be permissive (MIT, BSD, Apache-2.0)
  and redistributable. Apache-2.0 dependencies (PDFium) require a third-party notices file shipped
  with the package.
- **Engine: PDFium.** MuPDF is rejected because its AGPL licence would force copyleft on everyone.
- Windows only. Targets: `net8.0-windows` and `net10.0-windows` (net10 is what the first user
  runs; net8 is the long-term-support release many developers are still on).
- Native binaries for win-x64, win-x86 and win-arm64 (x86 matters because older deployments still
  run 32-bit programs).
- Public repository: every commit authored by P4 Software, no secrets, no code copied from any vendor
  or from private applications.
- Everything a person is meant to run (the test-print sample, see below) works by double-click,
  reads a commented `settings.json` next to it, explains every message in plain language and never
  closes before the result can be read.

## Approaches considered

**A. PDFium engine, printing through the Windows print system (chosen).**
Render each PDF page with PDFium straight onto the printer's page (vector quality, small spool
files), through `System.Drawing.Printing.PrintDocument`. PDFium is the renderer inside Chrome:
fast, accurate, permissively licensed. Trade-off: we ship and test native binaries ourselves.

**B. Windows' built-in WinRT PDF renderer.** Nothing native to ship, but it needs Windows 10 or
newer and is unreliable in services and scheduled tasks, which is the main use case. Rejected.

**C. MuPDF or another commercial/AGPL engine.** Excellent quality, but licence terms make it
unusable for a free MIT library. Rejected.

Inside A there are two sub-decisions:

- **How to call PDFium.** Options: an existing .NET binding (PDFiumCore is Apache-2.0 and
  netstandard2.1, with a fork that adds net10), or our own thin wrapper over prebuilt PDFium
  binaries. An internal interface sits in front of the engine (in the code: `IPdfEngine` and
  `IPdfDocument`), so the binding can be swapped without touching the public API. The binding is
  chosen after verifying each candidate's licence file and net10 behaviour (the comparison and the
  outcome are in THIRD-PARTY-NOTICES.md).
- **How to put pixels on paper.** Render directly onto the printer's device context with PDFium's
  printing flag; if a driver refuses, fall back to rendering a bitmap at the printer's resolution.
  Both paths must be tested against real printers.

## Public API (design sketch; the code has the final names)

```csharp
namespace PDFStream.NET;

public static class PdfPrinter
{
    public static PrintResult Print(string path, PrintOptions options);
    public static PrintResult Print(Stream pdf, PrintOptions options);
    public static PrintResult Print(byte[] pdf, PrintOptions options);
}

public sealed class PrintOptions
{
    public string PrinterName { get; init; }          // required
    public string DocumentName { get; init; }         // shown in the print queue
    public string Password { get; init; }             // for protected PDFs
    public string Pages { get; init; }                // e.g. "1-5, 8, 10-12"; empty = all
    public int Copies { get; init; } = 1;
    public DuplexMode Duplex { get; init; }           // printer default / none / long edge / short edge
    public bool? Color { get; init; }                 // null = printer default; true / false force it
    public ScaleMode Scale { get; init; }             // fit / actual size
    public string PaperSource { get; init; }
}

public sealed class PrintResult
{
    public bool Succeeded { get; }
    public PrintStatus Status { get; }                // enum, one value per failure reason
    public string Message { get; }                    // plain language: what happened, what to do next
    public int PagesPrinted { get; }
}
```

`PrintStatus` covers at least: OK, file not found, not a valid PDF, password required, wrong
password, printer not found, printer could not be opened or its settings are invalid, invalid page
range, page does not exist, cancelled, unknown error. The first real user already turns each printing
outcome into a message for its operators; those are the same reasons, so with `PrintStatus` its
mapping is a table lookup.

## v1 scope and release order

- **1.0**: everything in Goals above, the test-print sample, docs, CI. The first NuGet release is the release
  candidate 1.0.0-rc.1; the stable 1.0.0 follows once the remaining manual release checklist rows are verified.
- **After 1.0**: PDF to image (PNG, JPG, multi-page TIFF) reusing the same engine. Kept out of 1.0 so the
  first release is small and proven on printers; the public README says it is planned after 1.0.

No compatibility layer for other PDF libraries' APIs in v1: the first user has a single print call,
which is simpler to change directly.

## Repository layout

```
src/PDFStream.NET/               the library
tests/PDFStream.NET.Tests/       unit and integration tests
samples/PDFStream.NET.TestPrint  double-click sample: lists printers, prints a chosen PDF
docs/                       usage guide, printing from services and IIS, troubleshooting
docs/design/                this design
.github/workflows/          CI: build and test on windows-latest, pack NuGet
LICENSE  README.md  CHANGELOG.md  THIRD-PARTY-NOTICES.md  CONTRIBUTING.md
```

## Acceptance tests

1. **Real print, verified.** Print a multi-page PDF to *Microsoft Print to PDF* and confirm the
   output has the right page count and is not blank (count non-white pixels; do not trust the
   status alone).
2. Page ranges, copies, duplex and colour options reach the printer settings.
3. Password-protected PDF: right password prints; wrong or missing password returns the right status.
4. Bad input (missing file, garbage bytes, unknown printer) returns a status and message, never throws.
5. Runs from a Windows service and from a scheduled task under a normal service account.
6. x64, x86 and arm64 packages load and print.
7. **First-user switch**: the warehouse print service's single print call is changed to PDFStream.NET
   and, on one real PC, packing slips and bills of lading print correctly and look the same as
   before. Done only when the printouts have been checked on paper by eye.
8. The package contains no keys, no telemetry, and the licence file and third-party notices.

## What the first user needs

- **Call shape today.** One call per document: the PDF as a byte array, a printer name, an empty
  password and a document name. It sets no paper size, orientation, margins, tray, duplex or copies,
  so the printer's own defaults apply. PDFStream.NET's defaults must therefore be: printer defaults for
  everything (colour included: `Color` stays `null`, which leaves the printer's own colour setting,
  unless the caller sets it), and the page scaled to fit the printable area and rotated to match the paper
  (the safest match for packing slips and bills of lading; acceptance test 7 is judged by eye against
  the old output).
- **Hosting.** A Windows service at customer sites, printing to the document printers there, so
  acceptance test 5 (service account) is the real deployment, not an edge case. Label printing is
  out of scope.
- **Printer names.** Accept exactly the names Windows returns for installed printers, including
  network names such as `\\server\printer`.
- **Status messages.** The service turns each printing outcome into a message for its operators;
  with `PrintStatus` that becomes a one-to-one table lookup.

## Design decisions

- **Concurrency.** PDFium is not thread-safe, so the library takes a process-wide lock around every
  engine call. Calls from several threads are safe. The lock is held for one engine call (opening the
  PDF, measuring or drawing one page, closing it), not for a whole job, so concurrent jobs run side by
  side and interleave at page level; each stays its own print job. The one exception is the first form page
  of a document: its engine call finds and copies every planned form page, so it is longer (about 77 ms on x64
  and 95 ms on x86 for 500 form pages, measured in review), holds the lock throughout and cannot be
  interrupted. Documented.
- **Cancellation.** Every overload takes an optional `CancellationToken`. `Cancelled` means the
  token fired or the job was cancelled before it was sent. `PagesPrinted`: after a cancellation that
  ended the job normally it is the exact number of pages sent, and those pages print; after any failure it
  is 0, because an aborted job may have been withdrawn from the queue and the pages drawn before the
  failure normally never come out. The token is checked between pages, so it cannot interrupt the first form
  page's longer engine call either.
- **Printer state before printing.** A job is refused with
  `PrinterUnavailable` and a message naming the cause only when the queue is paused, set to "Use Printer
  Offline", not available, being deleted, or cannot be opened (a stopped Print Spooler included). An
  "Offline" status alone is not refused: network printers often report it through SNMP while they print
  normally. A printer that really is switched off therefore queues the job until it comes back.
- **Printing to a file without a dialog.** *Microsoft Print to PDF* shows a Save As dialog unless
  told where to write. `PrintOptions` gets an optional `OutputFile` (print to file). This is also a
  useful feature and what the tests use to run unattended.
- **Hardware and CI.** x64 build, tests and a real print to PDF run in CI on `windows-latest` if
  the runner has the driver; if it does not, those tests move to a manual checklist. x86, arm64,
  Windows-service and scheduled-task checks are a manual checklist run on real machines before each
  release, and the release notes say which were run. We do not claim what was not verified.
- **Owner permission flags** (a PDF that says "printing not allowed"): ignored in v1, which matches
  what the commercial libraries do and what server automation expects; documented clearly.
- **Form fields.** PDFium's page-drawing calls never draw form fields, so pages with fields are flattened
  before they are drawn, inside PDFStream.NET's private in-memory copy of the PDF. Flattening a page in place is
  wrong when the page inherits its /Resources: `FPDFPage_Flatten` creates an empty /Resources on the page, which
  hides the inherited fonts, images and XObjects. So the pages with fields among the pages a print job draws are
  copied to the end of the document first, in one `FPDF_ImportPagesByIndex` call with the document as source and
  destination (the import writes each page's inherited Resources, MediaBox, CropBox and Rotate onto its copy),
  and the fields are flattened into the copies, which are drawn in place of the pages. One call, not one per
  page: every call clones everything a page can reach, so one call per page multiplied a shared resource
  dictionary by the number of form pages (40 pages sharing 8 MB of images grew memory by about 290 MB, and a
  500-page job in a 32-bit process did not finish), while one call clones what the pages share once (14 to
  17 MB for the same 40 pages). The copy is made at most once per document, and only for a PDF of at most 64 MB
  (the copy budget: PDFium does not report what the copy costs, so the file size stands in for it). A copy is
  used only if it carries as many annotations as its page (the import drops a whole /Annots array when one
  entry is a damaged reference, and the pixel check cannot see fields) and draws exactly the same pixels as the
  page in a low-resolution comparison. The comparison catches what depends on object identity: the copies' OCG
  objects are new, so a layer the catalog switches off would show. It does not fully catch copies that share
  one content array (see the known limitation below). Every copy that is missing or cannot be used falls
  back to flattening into the page itself: the fields print, and a page that inherits its /Resources loses them
  as it did before the copy existed (the documented limitation). Only when that fails too is the page drawn as
  it is. `FPDF_FFLDraw` was not used because it draws only into a bitmap, so the device-context path would no
  longer be vector. `FPDF_ImportPagesByIndex` is marked experimental in `fpdf_ppo.h` and verified only with the
  bundled 160.0.8165. Costs and gaps: the pages to print are read once more to find the fields, and each form
  page is drawn twice at low resolution for the comparison; a value a page inherits inline rather than as a
  separate object is cloned onto every copy, which the budget does not bound (in a test, a deliberately
  inflated inline dictionary grew memory by about 600 MB for 100 form pages); XFA-only forms are not supported
  (the bundled PDFium has no XFA). **Known limitation (shared /Contents array).** When three or more form
  pages share one indirect /Contents array and one indirect /Resources dictionary, later pages can also show the
  fields of earlier pages, with status Ok (page 3 shows page 2's field; page 4 shows those of pages 2 and 3). The
  single import call makes the copies share the cloned content array, and `FPDFPage_Flatten` edits that array in
  place, naming its drawing after the first free `/FFTn` of the shared `/XObject` dictionary. The comparison
  catches the first contaminated copy (page 2), but the in-place fallback then flattens the original, shared
  array, so page 3's original and copy differ as well and page 3 also goes in place and keeps page 2's drawing.
  It was accepted as documented: a real fix needs private objects per copy (one import call per page), which
  brings back the memory growth of the one-call design, and PDFium has no API to unshare the array. A
  characterization test (`Known_limitation_...` in `FormFieldRenderingTests`) pins the behaviour; a fix must
  update the test and the user docs. Candidate fixes for 1.0: detect shared /Contents by comparing copy renders
  after each flatten, or an import-per-page mode when the budget allows. If reloading the flattened page or copy
  fails after a flatten, the unflattened page is drawn with blank form fields and status Ok; this is accepted
  because the same page index loaded moments earlier.
- **Publishing.** CI builds and packs the NuGet package; publishing a release is a manual step by a
  maintainer with the project's NuGet account. Copyright holder is P4 Software.

## Chosen during implementation

- Choice of PDFium binding, after checking each candidate's licence and net10 support.
- Exact public type and member names; internal structure; test framework.
- Details of the print path (device-context rendering vs bitmap fallback) and which drivers need
  the fallback.
- The wording of messages (plain language, English now; Spanish later is welcome).
- CI and NuGet publishing mechanics.

## Open questions

1. **Funding and sustainability** of the project (open since 2026-11-20; does not block 1.0).
2. **NuGet package ID**: decided on 2026-11-21: the package is `PDFStream.NET` (capital NET, like the owner's
   SheetStream.NET; the owner's final decision), the only name shown on nuget.org; the assembly (`PDFStream.NET.dll`)
   and the C# namespace (`PDFStream.NET`) carry the same name, before any user depends on the library. NuGet looks
   package IDs up without regard to case but keeps the capitalisation of the first publish for good, which is why
   the spelling is fixed before the first release. Checked on 2026-11-21: nuget.org has no package with this ID (its
   package content API, which takes the ID in lowercase, answered 404 Not Found for `pdfstream.net`), so it is free
   until someone publishes one. (Plain `PDFStream` was also free on 2026-11-20, but it is no longer the chosen ID.)
