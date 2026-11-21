# Troubleshooting

Find the message you got below; each section says what it means and what to check. The messages are the
exact texts PDFStream.net returns in `PrintResult.Message` (text in angle brackets is filled in for the actual
job). Problems that come with no error at all are at the end, starting with
[It says printed but nothing came out](#it-says-printed-but-nothing-came-out).

## Problems with the PDF

### "The PDF file could not be found or read."

> The PDF file could not be found or read. Check that the file path is correct and that this account is allowed to open the file.

Status `FileNotFound`. The path is blank, wrong, points at a folder, or the account running the program may
not read the file, or another program holds it locked. In a Windows service, remember that a relative path is
taken from the service's current folder (usually `C:\Windows\System32`), and that mapped drive letters
such as `Z:` belong to a user session: use a full local path or a UNC path (`\\server\share\file.pdf`)
that the service account can read.

### "The file is not a valid PDF, or it is damaged and cannot be opened."

> The file is not a valid PDF, or it is damaged and cannot be opened. Check that the file is a complete PDF and try opening it in a PDF viewer.

Status `InvalidPdf`. The bytes are not a PDF (an HTML error page saved as .pdf is a common case), the file
is cut short, the stream or byte array was empty, or the PDF has no pages. If you pass a stream, check that it
is positioned at the start of the PDF: it is read from its current position.

### "The PDF is protected with a password and none was given."

> The PDF is protected with a password and none was given. Enter the password in the print options and try again.

Status `PasswordRequired`. Set `PrintOptions.Password` to the PDF's open password.

### "The password given for the PDF is not correct."

> The password given for the PDF is not correct. Check the password and try again.

Status `PasswordInvalid`. The PDF needs an open password and the one given is wrong. (A PDF protected only by
an owner password prints whatever password you pass, so this message always means the open password.)

## Problems with the page range

### "The page range is not valid."

> The page range is not valid. Use page numbers starting at 1, separated by commas, and write ranges from the lower to the higher number, for example 1-5, 8, 10-12.

Status `InvalidPageRange`. The text of `Pages` is not a page range: letters, 0, a minus sign, an empty piece
between commas, or a range written backwards such as `5-2`.

### "The page range includes a page that is not in the PDF."

> The page range includes a page that is not in the PDF. Check the number of pages in the document and choose pages within it.

Status `PageNotFound`. The range is well written but names a page after the last one.

## Problems with the options

### "The print options are not valid, ..."

> The print options are not valid, for example the printer name is empty or the number of copies is less than 1. Check the print options and try again.

Status `InvalidOptions`. One of: the options object is `null`, `PrinterName` is empty, `Copies` is below 1
or above 32767, or `Duplex` / `Scale` holds a number that is not one of their named values.

### "The paper source "&lt;name&gt;" is not available ..."

> The paper source "&lt;name&gt;" is not available on this printer. The available paper sources are: &lt;list&gt;. Use one of these names exactly, or leave the paper source empty to use the printer's default tray.

> The paper source "&lt;name&gt;" is not available because this printer does not report any selectable paper sources. Leave the paper source empty to use the printer's default tray.

Status `InvalidOptions`. Copy one of the listed names into `PaperSource` (letter case does not matter), or
leave it out. Tray names come from the printer driver, so they can differ between two models, or between two
drivers for the same model.

### "The output file "&lt;path&gt;" cannot be used."

> The output file "&lt;path&gt;" cannot be used. Check that the path is a file name (not a folder) and that the folder it is in already exists.

Status `InvalidOptions`. `OutputFile` is blank, names a folder, contains characters Windows does not allow
in a file name, or its folder does not exist. PDFStream.net never creates folders: create it first.

## Problems with the printer

### "No printer with that name is installed on this computer."

> No printer with that name is installed on this computer. Check the printer name exactly as Windows lists it, and that the printer is visible to the account running this program.

Status `PrinterNotFound`. Check the spelling against Settings, Printers & scanners (or run the test-print
sample, which lists the installed printers). If the name is right and the program runs as a Windows service,
a scheduled task or an IIS app pool, the printer is probably a per-user connection that only your own account
can see. See [services-and-iis.md](services-and-iis.md#the-account-must-see-the-printer).

### "The printer could not be opened, or it does not accept the requested settings."

> The printer could not be opened, or it does not accept the requested settings. Check that the printer is switched on and connected and that the Windows Print Spooler service is running, then try again or choose different print settings.

Status `PrinterUnavailable`. The general printer message. It covers:

- `Duplex` set to `LongEdge` or `ShortEdge` on a printer whose driver cannot print on both sides (PDFStream.net
  refuses rather than printing one-sided). Use `DuplexMode.PrinterDefault` or `None`, or another printer.
- Windows lists the printer but cannot open it, or rejects its settings.
- The print system failed while the job was running (for example the driver failed to start or end the job).
- The printer reports no printable area at all.
- The list of printers could not be read for a reason other than a stopped Print Spooler (a stopped spooler
  that Windows reports as such gets its own message, below).

### "The printer "&lt;printer name&gt;" is paused in Windows, ..."

> The printer "&lt;printer name&gt;" is paused in Windows, so the document was not sent. Open the printer's queue (Settings, Printers & scanners), choose Resume, then print again.

Status `PrinterUnavailable`. A paused queue would accept the job and hold it, so PDFStream.net refuses instead of
reporting a print that never comes out. Resume the queue.

### "The printer "&lt;printer name&gt;" is set to "Use printer offline" in Windows, ..."

> The printer "&lt;printer name&gt;" is set to "Use printer offline" in Windows, so the document was not sent. Open the printer's queue, clear "Use printer offline" in the Printer menu, then print again.

Status `PrinterUnavailable`. Someone chose "Use printer offline" for this queue. Clear it.

### "The printer "&lt;printer name&gt;" is not available at the moment, ..."

> The printer "&lt;printer name&gt;" is not available at the moment, so the document was not sent. Check that the printer is switched on and connected, then print again.

Status `PrinterUnavailable`. Windows itself marks the printer as not available.

### "The printer "&lt;printer name&gt;" is being removed from Windows ..."

> The printer "&lt;printer name&gt;" is being removed from Windows and no longer accepts documents, so the document was not sent. Choose another printer, or add this printer again, then print again.

Status `PrinterUnavailable`. The printer is being deleted (often it waits for its last job to finish).

### "Windows could not open the printer "&lt;printer name&gt;", ..."

> Windows could not open the printer "&lt;printer name&gt;", so the document was not sent. Check that the Windows Print Spooler service is running and that the account running this program is allowed to use this printer, then print again.

Status `PrinterUnavailable`. The printer is in the list but its queue cannot be opened, usually because the
account has no permission to print to it (check the printer's Security tab, and on a print server the share's
permissions), or the print server cannot be reached.

### "The Windows Print Spooler service is not running, ..."

> The Windows Print Spooler service is not running, so no printer can be used and the document was not sent. Start the Print Spooler service (in Services, or ask the person who looks after this computer), then print again.

Status `PrinterUnavailable`. Start the Print Spooler service (`services.msc`, "Print Spooler", Start; set it
to start automatically).

What PDFStream.net detects: Windows answers "RPC server unavailable" (error 1722) while the spooler service is
stopped, and PDFStream.net gives this message when it gets that answer while reading the list of printers, while
opening the printer's queue, or when the printer you named is not in the list and the local spooler, asked
directly, gives that answer. What it does not do: it does not ask the Windows service manager about the
service, so a spooler that fails in some other way (still starting, crashing, refusing this account) gets the
general printer message above, which also says to check the spooler.

Not verified yet: this mapping is tested only with simulated answers. A machine with the spooler really stopped
is on the [release checklist](release-checklist.md) and has not been tried.

## The job stopped

### "Printing was cancelled before it finished, ..."

> Printing was cancelled before it finished, so some pages may not have been printed. Start the job again if the document is still needed.

Status `Cancelled`. Your cancellation token fired, or the print system cancelled the job by itself. Check
`PagesPrinted`: when your token stopped the job between pages it is the exact number of pages that print;
when it is 0, nothing was sent. A print-to-file printer used without `OutputFile` asks where to save, and
closing that dialog without saving cancels the job. Where there is no screen to show the dialog on (a service,
for example), the job cannot finish normally: it may be cancelled or it may wait. Always set `OutputFile` for
such printers.

### "Page &lt;n&gt; of the PDF could not be drawn, ..."

> Page &lt;n&gt; of the PDF could not be drawn, so printing was stopped and the unfinished job may have been withdrawn from the print queue. Check the print queue for a job left over from this document and delete it, then open the PDF in a PDF viewer to check that the page is not damaged and print the document again. Details: &lt;technical reason&gt;

Status `UnknownError`, `PagesPrinted` 0. The PDF engine failed on that page. Check the queue for a leftover
job, open the PDF in a viewer, and keep the "Details" text for a bug report.

### "Page &lt;n&gt; could not be printed: the printer did not accept it as drawing commands, ..."

> Page &lt;n&gt; could not be printed: the printer did not accept it as drawing commands, and the page is too large to send as a picture with the memory available. Printing was stopped and the unfinished job may have been withdrawn from the print queue, so check the print queue for a job left over from this document and delete it. Choose a smaller paper size, or split the document into smaller parts, then try again.

Status `UnknownError`, `PagesPrinted` 0. The driver refused the page as drawing commands, so PDFStream.net tried
to send it as a picture, and there was not enough memory for one. This happens most in 32-bit programs with
very large pages; a 64-bit build of your program usually solves it.

### "Printing failed for an unexpected reason. ... Details: &lt;technical reason&gt;"

> Printing failed for an unexpected reason. Try again, and if it keeps happening contact the person who looks after this program. Details: &lt;technical reason&gt;

Status `UnknownError`. Something none of the other statuses describes. The "Details" part is the technical
reason. The most likely one in a new installation is that `pdfium.dll` is missing for the processor the
program runs on. The details then read "Unable to load DLL 'pdfium' or one of its dependencies: The specified
module could not be found. (0x8007007E)"; see
[services-and-iis.md](services-and-iis.md#32-bit-and-64-bit) for how the right copy gets next to your program.

## Problems that come with no error

### It says printed but nothing came out

`Ok` means every page was rendered and handed to Windows. Paper coming out is up to the printer, and the job
can wait in the Windows queue:

- **The printer shows "Offline" in Windows but it really is on.** Network printers often report Offline
  through SNMP while they print perfectly well, so PDFStream.net does not refuse a printer just because Windows
  says Offline. Only these queue states are refused (with their own messages above): paused, "Use printer
  offline", not available, being deleted, and a queue that cannot be opened (including a stopped Print
  Spooler).
- **The printer really is switched off or unplugged.** The job is accepted and waits in the queue; it prints
  when the printer comes back. Open the queue to see it.
- **Out of paper, jammed, out of toner, door open.** These are not refused either: the job waits in the queue
  and prints once someone fixes the printer.
- **Wrong printer.** Check `PrinterName`; network names look like `\\server\printer`.

### The output file is empty, locked or missing right after printing

*Microsoft Print to PDF* keeps writing the file for a moment after `Print` returns. Open the file only once it
can be opened on its own; [usage.md](usage.md#printing-to-a-file) has a short helper that waits for it.

### More pages were counted than the PDF has

With software copies (a driver that supports fewer copies than you asked for, such as Print to PDF),
`PagesPrinted` counts every page sent, so 3 pages x 2 copies is 6. When the driver makes the copies itself,
each page is sent once and it is 3. See [usage.md](usage.md#copies).

### Duplex copies start on the back of the previous copy

Known limitation: with software copies, duplex on and an odd page count, the next copy starts on the back of
the last sheet of the previous copy. Print one copy per call when that matters.

### Form fields are missing on the paper

PDFStream.net prints filled-in form fields (see [usage.md](usage.md#form-fields)). If some are missing:

- **The field is marked as not printable.** PDF forms can hide a field from paper (often instructions or
  buttons). Such fields stay off the paper on purpose, as they do when the PDF is printed from a viewer.
- **The page says "Please wait..." or the fields are empty and the form looks different in Adobe Reader.**
  The PDF is an XFA form. XFA forms are not supported: the bundled PDF engine is built without XFA, so it prints
  only the page content. Save or print the form to a plain PDF first (for example with "Microsoft Print to PDF"
  from a viewer that shows it correctly) and print that file.
- **A value shows in one format on screen and another on paper.** Form scripts do not run, so a value that a
  script formats (dates, currency) prints as it is stored in the PDF.
- **Every field of one page is blank, but the rest of that page prints.** The PDF engine refused to draw that
  page's fields at all, both into a copy of the page and into the page itself, so PDFStream.net printed the page
  without them instead of failing the print. If you can share the PDF, open an issue with it.
- **The fields print, but images or text are missing from that page.** This is the known limitation of form
  printing (see [usage.md](usage.md#form-fields)). A page with fields is normally printed from a copy that has
  its fields drawn in. When that copy cannot be used, the fields are drawn into the page itself instead, and if
  the page takes its fonts or images from the PDF's page tree (instead of carrying them itself), those are then
  hidden. The copy cannot be used, for example, when the PDF switches layers off on that page, when the page
  refers to something the file does not contain (a damaged PDF), when the PDF is larger than 64 MB (the copy
  budget), or when your application uses a `pdfium.dll` other than the one PDFStream.net was tested with (see
  [usage.md](usage.md#form-fields)). Print or save the filled form to a plain PDF first (for example with
  "Microsoft Print to PDF" from a viewer, or with the "flatten" option of the tool that filled it) and print
  that file.

### A PDF marked "printing not allowed" printed anyway

That is by design: owner permissions such as "printing not allowed" are ignored, as server automation
expects. Only an open password stops PDFStream.net from reading a PDF.
