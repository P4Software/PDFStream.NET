# Security policy

## Reporting a vulnerability

Please do not report security problems in public issues, discussions or pull requests.

Report them privately through GitHub's private vulnerability reporting on this repository: open the
**Security** tab of [P4Software/PDFStream](https://github.com/P4Software/PDFStream) and choose
**Report a vulnerability** (or go straight to
[the reporting form](https://github.com/P4Software/PDFStream/security/advisories/new)). Only the maintainers and
you can see the report.

Please include:

- the PDFStream.net version (or commit), the .NET version, the Windows version and the processor architecture;
- what happens (a crash of the whole process, a hang, a file read or written that should not be, and so on);
- if you can, a PDF or the steps that reproduce it. A PDF that crashes a print service is often a real
  document: remove anything confidential before you attach it, or describe how to build an equivalent file.

We will answer in the report, keep you informed while a fix is prepared, and credit you in the published
advisory unless you prefer not to be named.

## Supported versions

| Version | Security fixes |
| --- | --- |
| Latest release on NuGet | Yes |
| Older releases | No: update to the latest release |
| Previews and builds from source | No: use the latest release, or the latest commit on `main` |

PDFStream.net has not had its first release on NuGet (as the package `PDFStream.net`) yet. Until it has, fixes land on `main` only.

## PDFium, the PDF engine

PDFStream.net does not build or change PDFium: it calls the prebuilt `pdfium.dll` from the
[`bblanchon.PDFium.Win32`](https://www.nuget.org/packages/bblanchon.PDFium.Win32) NuGet package. Security fixes in
PDFium are taken by updating that dependency to a newer build and releasing a new PDFStream.net version. An
application can also reference a newer `bblanchon.PDFium.Win32` directly, which NuGet then uses instead; PDFStream.net
has only been tested with the version it depends on.

## Known limits

PDFium runs inside the program that calls PDFStream.net, without a sandbox, so a malformed or hostile PDF that crashes
it ends that whole process. This and the other limits (one hung printer driver stalls every print in the
process; cancellation is checked between pages only) are explained, with what to do about them, in
[Security and limits](docs/usage.md#security-and-limits). A PDF that crashes or hangs PDFStream.net is still worth
reporting as described above.
