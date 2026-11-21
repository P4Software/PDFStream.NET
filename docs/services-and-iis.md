# Printing from Windows services, scheduled tasks and IIS

PDFStream.NET is built for programs that print with nobody at the screen: a Windows service, a scheduled task,
a web application in IIS. It never shows a dialog and never waits for a person. This page explains what such
a program needs from Windows.

**Status: not yet verified.** Running from a Windows service, from a scheduled task and from IIS are on the
[release checklist](release-checklist.md) and have not been run yet. Microsoft's documentation also states
that the `System.Drawing.Printing` classes, which PDFStream.NET prints through, are not supported in a Windows
service or an ASP.NET application. PDFStream.NET turns every failure into a result with a message instead of an
exception, but until those checklist rows are run, test printing under your real account and printer before
you rely on it.

**Untrusted PDFs.** PDFium runs inside your service without a sandbox, so a malformed or hostile PDF that
crashes it ends the whole service process, which .NET cannot catch; print PDFs you do not trust in a separate
worker process (see [Security and limits](usage.md#security-and-limits)).

## The account must see the printer

A program prints as the account it runs under, and it can only use the printers that account can see. That
is the most common reason a service gets `PrinterNotFound` for a printer that is plainly installed.

- **Printers installed on the machine** (a local USB printer, or a network printer added with a Standard
  TCP/IP port, by an administrator) are visible to every account, services included. This is the most
  reliable set-up for a service.
- **Per-user printer connections** are not visible to a service. A connection to a shared printer
  (`\\server\printer`) added from a user's own session, through "Add a printer" or by double-clicking the
  share, belongs to that user only. A service, a scheduled task running as another account, and an IIS app pool
  do not see it, even on the same machine.
- **Machine-wide connections** to a shared printer can be added by an administrator, for example with
  `rundll32 printui.dll,PrintUIEntry /ga /n"\\server\printer"`, after which the Print Spooler service must be
  restarted; or through Group Policy (Deployed Printers, "per computer"). Every account then sees the printer.
- **Permission to print.** The account also needs the Print permission on the printer (Printer properties,
  Security tab) and, for a shared printer, on the print server's share.
- **Which identity reaches the network.** `LocalSystem` and `NetworkService` reach a print server as the
  computer account (`DOMAIN\COMPUTERNAME$`), so that account needs permission on the server's printer.
  `LocalService` reaches the network anonymously and normally cannot use a shared printer at all. A normal
  domain user account used as the service account reaches the network as itself.

Give the printer name exactly as Windows lists it for that account; network names look like
`\\server\printer`. To see what the account sees, run the test-print sample as that account (for example
with "Run as different user"), or log `PrinterSettings.InstalledPrinters` from the service itself.

## Windows services

- Run the service under an account that can see and use the printer (see above). A dedicated, normal (not
  administrator) account is the safest choice and the one the release checklist tests.
- Set `PrinterName` on every call: a service has no default printer of its own.
- Never print to a print-to-file printer (such as *Microsoft Print to PDF*) without `OutputFile`. Without it,
  the printer asks where to save the file, and a service has no screen to ask on, so the job cannot finish
  normally: it may be cancelled or it may wait. (This case has not been tried in a service.)
- Use full paths for `OutputFile` and for PDF files. A service's current folder is usually
  `C:\Windows\System32`, and mapped drive letters belong to user sessions, not to services.
- Keep the message. `PrintResult.Message` says what to fix in words the person reading the log understands:
  write it to the log or the event log as it is.

## Scheduled tasks

A task set to "Run whether user is logged on or not" behaves like a service: it runs as its account without a
screen, so everything above applies. A task set to "Run only when user is logged on" runs in that user's
session and sees that user's printers, including per-user connections.

## IIS

A web application prints as its application pool's identity.

- The default identity, `ApplicationPoolIdentity`, is a virtual account named `IIS AppPool\<pool name>`. It
  sees the printers installed on the machine (and machine-wide connections), not anyone's per-user
  connections. To give it permission on a printer, add `IIS AppPool\<pool name>` on the printer's Security tab.
  On the network it acts as the computer account, as described above.
- Alternatively, run the pool as a dedicated domain account that has permission on the printer.
- Printing takes seconds, not milliseconds. Do not print inside the request if you can avoid it: put the job in
  a queue and print from a background worker, so a slow or stuck printer never holds up a web request.

## 32-bit and 64-bit

The program must load the `pdfium.dll` built for the processor mode it runs in. The `PDFStream.NET` package brings all three
(x64, x86 and Arm64) and your build places the right one:

- **A build or publish for one runtime identifier** (for example `-r win-x64` or `-r win-x86`) puts that
  architecture's `pdfium.dll` next to your program. Make sure it matches how the program really runs: an IIS
  pool with "Enable 32-Bit Applications" set to True runs a 32-bit process and needs the x86 copy.
- **A build without a runtime identifier** puts all three under `runtimes\win-x64`, `runtimes\win-x86` and
  `runtimes\win-arm64`, and .NET loads the right one when the program starts. Copy the whole output folder,
  including `runtimes` and the `.deps.json` file, when you deploy.

If the copy is missing, every print returns `UnknownError` with details that mention `pdfium`.

## Several jobs at once

A service or web application often prints from several threads. That is safe. PDFium, the engine that reads
and draws PDFs, is not thread-safe, so PDFStream.NET makes every call into it under one lock for the whole
process. The lock is held for one engine call at a time (opening a PDF, drawing one page, closing it), not for
a whole job, so jobs that run at the same time interleave at page level: each is still its own print job with
its pages in order, but each takes longer than it would alone. If timing matters, print from one queue.

## "Printing not allowed" PDFs

Owner permissions inside a PDF, such as "printing not allowed", are ignored: such a PDF prints like any other.
That is what server automation expects (a service printing a supplier's packing slip cannot ask anyone to lift
a restriction). Only an open password stops PDFStream.NET from
reading a PDF, and you supply that in `PrintOptions.Password`.
