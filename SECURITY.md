# Security policy

## Reporting a vulnerability

Please do not report security problems in public issues, discussions or pull requests.

Report them privately through GitHub's private vulnerability reporting on this repository: open the
**Security** tab of [P4Software/PDFStream.NET](https://github.com/P4Software/PDFStream.NET) and choose
**Report a vulnerability** (or go straight to
[the reporting form](https://github.com/P4Software/PDFStream.NET/security/advisories/new)). Only the maintainers and
you can see the report.

Please include:

- the PDFStream.NET version (or commit), the .NET version, the Windows version and the processor architecture;
- what happens (a crash of the whole process, a hang, a file read or written that should not be, and so on);
- if you can, a PDF or the steps that reproduce it. A PDF that crashes a print service is often a real
  document: remove anything confidential before you attach it, or describe how to build an equivalent file.

We will answer in the report, keep you informed while a fix is prepared, and credit you in the published
advisory unless you prefer not to be named.

## Supported versions

| Version | Security fixes |
| --- | --- |
| 1.0.0-rc.x and later releases (the latest release on NuGet) | Yes |
| Older releases | No: update to the latest release |
| Builds from source | No: use the latest release, or the latest commit on `main` |

Version 1.0.0-rc.1 is the first release of PDFStream.NET (the package `PDFStream.NET`). It has not been published on
NuGet yet; until it is, fixes land on `main` only.

## PDFium, the PDF engine

PDFStream.NET does not build or change PDFium: it calls the prebuilt `pdfium.dll` from the
[`bblanchon.PDFium.Win32`](https://www.nuget.org/packages/bblanchon.PDFium.Win32) NuGet package. Security fixes in
PDFium are taken by updating that dependency to a newer build and releasing a new PDFStream.NET version. An
application can also reference a newer `bblanchon.PDFium.Win32` directly, which NuGet then uses instead; PDFStream.NET
has only been tested with the version it depends on.

Printing form fields uses one PDFium function that PDFium marks as experimental: `FPDF_ImportPagesByIndex`, which
copies pages within PDFStream.NET's in-memory copy of the PDF. It has been tested only with PDFium 160.0.8165, the
version PDFStream.NET depends on. With a `pdfium.dll` in which that function is missing or fails, form fields still
print, drawn into each page itself, but a page that takes its fonts or images from the PDF's page tree then prints
without them; see [Form fields](docs/usage.md#form-fields).

## Known limits

PDFium runs inside the program that calls PDFStream.NET, without a sandbox, so a malformed or hostile PDF that crashes
it ends that whole process. This and the other limits (one hung printer driver stalls every print in the
process; cancellation is checked between pages only) are explained, with what to do about them, in
[Security and limits](docs/usage.md#security-and-limits). A PDF that crashes or hangs PDFStream.NET is still worth
reporting as described above.

Two more limits are known and accepted for the first release:

- **A crafted PDF can make form printing use a lot of memory.** A value that pages inherit from the page tree,
  written inline, is copied once for every page with fields, and the copy budget does not bound it. In a
  measurement, a 1.2 MB PDF with 100 form pages grew memory by about 600 MB. Print PDFs you do not trust in a
  separate worker process, so that a file like this costs one job and not your service.
- **Pages that share one content array can show each other's form fields.** In a PDF with three or more form
  pages that share one indirect `/Contents` array, later pages can also print the fields of earlier pages
  (status `Ok`). If a printout must never carry another page's values, flatten such PDFs first. See
  [Form fields](docs/usage.md#form-fields).
