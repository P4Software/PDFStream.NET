# Using PDFStream

This guide covers everything the library does: the three print calls, every print option, every result
status with the exact message it carries, and the behaviour that is worth knowing before you rely on it in
production. For problems, start from the message you got in [troubleshooting.md](troubleshooting.md). For
Windows services, scheduled tasks and IIS, read [services-and-iis.md](services-and-iis.md).

## Requirements

- Windows 10 or Windows Server 2016, or newer.
- A project that targets a Windows framework: `net8.0-windows`, `net10.0-windows` or newer. A project that
  targets plain `net8.0` cannot reference the `PDFStream.net` package, because printing goes through the Windows
  print system.
- Nothing else on the machine: no Adobe Reader, no PDF viewer, no licence key. The PDF engine (PDFium) comes
  with the package for 64-bit (x64), 32-bit (x86) and Arm64 Windows, and your build picks the right one.
- The NuGet package is `PDFStream.net` (`dotnet add package PDFStream.net`); in code the namespace is
  `PDFStream`.

## The three calls

```csharp
using PDFStream;

PrintResult fromFile   = PdfPrinter.Print(@"C:\Slips\slip-1042.pdf", options);
PrintResult fromStream = PdfPrinter.Print(stream, options);   // read from its current position to the end
PrintResult fromBytes  = PdfPrinter.Print(pdfBytes, options);
```

Each call takes an optional `CancellationToken` as its last argument. None of them throws a managed exception
for bad input or printer problems: every such outcome, including an unexpected internal failure, comes back as a
`PrintResult`. The one failure no .NET library can turn into a result is a crash inside the native PDF engine;
see [Security and limits](#security-and-limits).

- **File path**: a missing, unreadable or blank path returns `FileNotFound`.
- **Stream**: it is read from its current position to its end, it does not need to support seeking, and it
  is left open (it belongs to you). A `null`, empty or broken stream returns `InvalidPdf`.
- **Byte array**: the complete PDF file. `null` or empty returns `InvalidPdf`. The array is not changed or kept.

A full example:

```csharp
PrintResult result = PdfPrinter.Print(pdfBytes, new PrintOptions
{
    PrinterName = @"\\print-server\Warehouse Printer 1",
    DocumentName = "Packing slip 1042",
    Pages = "1-2",
    Copies = 2,
    Duplex = DuplexMode.LongEdge,
    Color = false,
});

if (!result.Succeeded)
{
    // The message is written for people: log it. An UnknownError message can end with technical details,
    // so do not show it as it is in a public web page.
    logger.LogWarning("Packing slip 1042 did not print ({Status}): {Message}", result.Status, result.Message);
}
```

## Print options

Build a `PrintOptions` with an object initializer; every property is init-only. Anything you leave out keeps
the printer's own setting, except where a default is given below.

| Option | Default | What it does |
|---|---|---|
| `PrinterName` | empty (required) | The printer, exactly as Windows lists it, including network names such as `\\server\printer`. An exact match wins; otherwise letter case and spaces around the name are ignored. Empty returns `InvalidOptions`. |
| `DocumentName` | `null` | The name of the job in the print queue. `null` or blank uses "PDF document". |
| `Password` | `null` | The password of a protected PDF. `null` or empty means none. A PDF that only has an owner password (restrictions, but no password to open it) prints without one, even when you pass an unrelated password. |
| `Pages` | `null` (every page) | The pages to print, counted from 1, for example `"1-5, 8, 10-12"`. Pages print in the order written, and a page written twice prints twice. Spaces around numbers are fine. `null`, empty or blank means every page. |
| `Copies` | 1 | How many copies, from 1 to 32767. See [Copies](#copies). |
| `Duplex` | `PrinterDefault` | `PrinterDefault` changes nothing; `None` prints one-sided; `LongEdge` flips like a book; `ShortEdge` flips like a notepad. Asking a printer that cannot print on both sides for `LongEdge` or `ShortEdge` returns `PrinterUnavailable` instead of quietly printing one-sided. |
| `Color` | `null` (printer's own setting) | `null` keeps whatever the printer is set to; `true` asks for colour and `false` for black and white. A printer that cannot print in colour prints black and white either way. |
| `Scale` | `FitToPage` | `FitToPage` shrinks or enlarges each page to fit the printable area, centred, and turns it to match the paper when that fits better. `ActualSize` prints at 100%, centred, turning the page when that keeps more of it on the paper; anything outside the printable area is cut off. Every page is sized on its own, so a PDF with mixed sizes and orientations works. |
| `PaperSource` | `null` (default tray) | The tray name as the printer reports it (letter case ignored). A name the printer does not have returns `InvalidOptions` with the list of tray names it does have. |
| `OutputFile` | `null` | Writes the job to this file instead of the printer's port. This is how *Microsoft Print to PDF* runs without a Save As dialog. See [Printing to a file](#printing-to-a-file). |

## The result

| Member | Meaning |
|---|---|
| `Succeeded` | `true` only when every requested page was rendered and sent to the printer (`Status` is `Ok`). |
| `Status` | A `PrintStatus` value to branch on in code. |
| `Message` | The same outcome in plain language: what happened and, for a failure, what to do next. Most messages can be shown to the person running the program as they are, but an `UnknownError` message may end with technical details (the text of an internal exception), so log it rather than show it in a public web page. |
| `PagesPrinted` | How many pages were sent to the printer. See below. |

### What PagesPrinted counts

- **After success**: every page sent. When the library makes the copies itself (software copies, see
  [Copies](#copies)), that is pages x copies; when the printer driver makes the copies, each page is sent
  once, so it is the number of pages only.
- **After a cancellation that ended the job normally**: the exact number of pages sent before printing
  stopped. The job is then finished normally, so those pages do print.
- **After a failure**: 0. A job that fails is aborted and may have been withdrawn from the print queue by
  Windows (check the queue for a job left over), and pages drawn before the failure normally never come out. A cancellation that arrives before the first page has
  been drawn is also reported with 0, and so is a job the print system cancelled by itself.

"Printed" means the pages were really rendered and handed to Windows. It does not mean paper has come out:
a printer that is out of paper or switched off keeps the job in its queue (see
[troubleshooting.md](troubleshooting.md#it-says-printed-but-nothing-came-out)).

## Every status and its message

Each status has a standard message. Where a more specific message can replace it, that message is listed
too; text in angle brackets is filled in for the actual job.

| Status | Standard message |
|---|---|
| `Ok` | The document was printed. All requested pages were sent to the printer. |
| `FileNotFound` | The PDF file could not be found or read. Check that the file path is correct and that this account is allowed to open the file. |
| `InvalidPdf` | The file is not a valid PDF, or it is damaged and cannot be opened. Check that the file is a complete PDF and try opening it in a PDF viewer. |
| `PasswordRequired` | The PDF is protected with a password and none was given. Enter the password in the print options and try again. |
| `PasswordInvalid` | The password given for the PDF is not correct. Check the password and try again. |
| `PrinterNotFound` | No printer with that name is installed on this computer. Check the printer name exactly as Windows lists it, and that the printer is visible to the account running this program. |
| `PrinterUnavailable` | The printer could not be opened, or it does not accept the requested settings. Check that the printer is switched on and connected and that the Windows Print Spooler service is running, then try again or choose different print settings. |
| `InvalidPageRange` | The page range is not valid. Use page numbers starting at 1, separated by commas, and write ranges from the lower to the higher number, for example 1-5, 8, 10-12. |
| `PageNotFound` | The page range includes a page that is not in the PDF. Check the number of pages in the document and choose pages within it. |
| `InvalidOptions` | The print options are not valid, for example the printer name is empty or the number of copies is less than 1. Check the print options and try again. |
| `Cancelled` | Printing was cancelled before it finished, so some pages may not have been printed. Start the job again if the document is still needed. |
| `UnknownError` | Printing failed for an unexpected reason. Try again, and if it keeps happening contact the person who looks after this program. |

The more specific messages:

| Status | When | Message |
|---|---|---|
| `PrinterUnavailable` | The queue is paused | The printer "&lt;printer name&gt;" is paused in Windows, so the document was not sent. Open the printer's queue (Settings, Printers & scanners), choose Resume, then print again. |
| `PrinterUnavailable` | "Use printer offline" is set | The printer "&lt;printer name&gt;" is set to "Use printer offline" in Windows, so the document was not sent. Open the printer's queue, clear "Use printer offline" in the Printer menu, then print again. |
| `PrinterUnavailable` | Windows reports it not available | The printer "&lt;printer name&gt;" is not available at the moment, so the document was not sent. Check that the printer is switched on and connected, then print again. |
| `PrinterUnavailable` | The printer is being deleted | The printer "&lt;printer name&gt;" is being removed from Windows and no longer accepts documents, so the document was not sent. Choose another printer, or add this printer again, then print again. |
| `PrinterUnavailable` | The queue cannot be opened | Windows could not open the printer "&lt;printer name&gt;", so the document was not sent. Check that the Windows Print Spooler service is running and that the account running this program is allowed to use this printer, then print again. |
| `PrinterUnavailable` | The Print Spooler is stopped | The Windows Print Spooler service is not running, so no printer can be used and the document was not sent. Start the Print Spooler service (in Services, or ask the person who looks after this computer), then print again. |
| `InvalidOptions` | Unknown paper source | The paper source "&lt;name&gt;" is not available on this printer. The available paper sources are: &lt;list&gt;. Use one of these names exactly, or leave the paper source empty to use the printer's default tray. |
| `InvalidOptions` | Unknown paper source, printer lists none | The paper source "&lt;name&gt;" is not available because this printer does not report any selectable paper sources. Leave the paper source empty to use the printer's default tray. |
| `InvalidOptions` | Unusable output file | The output file "&lt;path&gt;" cannot be used. Check that the path is a file name (not a folder) and that the folder it is in already exists. |
| `UnknownError` | An unexpected exception | Printing failed for an unexpected reason. Try again, and if it keeps happening contact the person who looks after this program. Details: &lt;technical reason&gt; |
| `UnknownError` | A page could not be drawn | Page &lt;n&gt; of the PDF could not be drawn, so printing was stopped and the unfinished job may have been withdrawn from the print queue. Check the print queue for a job left over from this document and delete it, then open the PDF in a PDF viewer to check that the page is not damaged and print the document again. Details: &lt;technical reason&gt; |
| `UnknownError` | A page is too large for the picture fallback | Page &lt;n&gt; could not be printed: the printer did not accept it as drawing commands, and the page is too large to send as a picture with the memory available. Printing was stopped and the unfinished job may have been withdrawn from the print queue, so check the print queue for a job left over from this document and delete it. Choose a smaller paper size, or split the document into smaller parts, then try again. |

The checks run in this order, and the first problem ends the call: the options, the cancellation token,
opening the PDF, the page range against the real page count, the printer and its settings (including the
queue state), and then the job itself.

## Copies

When the printer driver says it can make the requested number of copies, the library asks the driver for
them and sends each page once. When it cannot (a driver that supports only 1 copy, such as *Microsoft Print
to PDF*), the library sends the whole document again for each copy, in the same print job, so the copies
come out collated and a print-to-file job still produces one file. It never does both, so you never get
copies of copies.

Known limitation: with software copies, duplex on, and an odd number of pages, the next copy starts on the
back of the last sheet of the previous copy. Print one copy per call if that matters for your documents.

## Passwords and "printing not allowed"

A PDF can carry an *open* password (needed to read it) and an *owner* password with permissions such as
"printing not allowed". PDFStream needs the open password when there is one. Owner permissions are ignored:
a PDF that says "printing not allowed" prints anyway, which is what server automation expects. Do not use
PDFStream to enforce a document's permissions.

## Printing to a file

Set `OutputFile` to have the job written to a file instead of sent to the printer's port, for example with
*Microsoft Print to PDF*. The folder must already exist; use a full path (a relative path is taken from the
current folder of the process, which in a Windows service is `C:\Windows\System32`). An existing file is
replaced. An empty text returns `InvalidOptions`; leave the option out (`null`) when you do not want a file.

**Microsoft Print to PDF keeps writing after Print returns.** The print call returns when the pages have been
sent; the driver finishes the file a moment later. Open the file only once it can be opened on its own:

```csharp
/// <summary>Waits until the print system has finished writing the file, or the time runs out.</summary>
static bool WaitUntilWritten(string path, TimeSpan timeout)
{
    DateTime giveUp = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < giveUp)
    {
        try
        {
            // Opening with no sharing only succeeds once the print system has closed the file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            if (stream.Length > 0)
            {
                return true;
            }
        }
        catch (IOException)
        {
            // Not created yet, or still being written: try again shortly.
        }

        Thread.Sleep(500);
    }

    return false;
}
```

## Cancelling

Pass a `CancellationToken`. It is checked before the PDF is opened, after it is opened, and between pages,
never in the middle of a page. A job stopped between pages ends normally, so the pages already sent print and
`PagesPrinted` counts them exactly; the status is `Cancelled`.

## Security and limits

Read this before you print PDFs that come from outside your organisation (uploads, e-mail, customer portals).

- **PDFium runs inside your process, without a sandbox.** It is the PDF engine of Chrome, but Chrome runs it in
  a sandboxed process of its own; PDFStream calls it directly in yours. A malformed or deliberately hostile PDF
  that triggers a memory fault inside PDFium ends the whole process at once: .NET cannot catch it, no
  `PrintResult` is returned, and a Windows service simply stops (its recovery settings can restart it).
- **Print untrusted PDFs in a separate worker process.** Run the print call in a small child process (one per
  job, or a long-lived worker that you start again when it dies) and treat a worker that disappears as a failed
  job. A bad file then costs one job instead of your service.
- **One hung printer driver stalls every print in the process.** PDFium is not thread-safe, so PDFStream holds
  one lock for the whole process around every PDFium call, including drawing a page onto the printer, which
  runs inside the printer driver. A driver that hangs while a page is drawn keeps that lock, and every other
  print in the same process waits behind it. A worker process per printer (or per job) contains this as well.
- **Cancellation is checked between pages only.** The token is checked before the PDF is opened, after it is
  opened and between pages. It cannot interrupt a page that is being drawn, a driver that hangs, or the opening
  of a very large PDF.
- **Security fixes in PDFium** reach PDFStream through updates of the `bblanchon.PDFium.Win32` package it
  depends on. To report a vulnerability, see [SECURITY.md](../SECURITY.md).

## Several threads at once

The calls are safe from several threads at once. PDFium, the engine that reads and draws the PDF, is not
thread-safe, so every call into it is made under one lock for the whole process. The lock is held for one
engine call at a time (opening the PDF, measuring or drawing one page, closing it), not for a whole job, so
concurrent jobs run side by side and interleave at page level. Each call is still its own print job with its
own pages in order; each simply takes longer than it would alone. If you print from many threads (a busy web
server, for example), a queue that prints one document at a time gives steadier timing.

## How pages reach the printer

Each page is drawn by PDFium straight onto the printer's page as drawing commands, which keeps text and
barcodes sharp and the spool file small. If a driver refuses that, the page is drawn as a picture at the
printer's resolution (at most 300 dpi) instead. Before any job starts, the printer's queue is checked: a
queue that is paused, set to "Use printer offline", not available, being deleted, or cannot be opened is
refused with a message naming the cause. A printer that only *reports* Offline is not refused; see
[troubleshooting.md](troubleshooting.md#it-says-printed-but-nothing-came-out).

## Form fields

Filled-in form fields print: text fields, checkboxes, radio buttons, lists and the other fields of an
ordinary PDF form (an AcroForm), with the values they hold. PDFium, the PDF engine, never draws form fields
when it draws a page, so before a page with fields is drawn for the first time, PDFStream makes a copy of that
page in its own copy of the PDF in memory, draws the fields into the copy exactly as they print, and prints the
copy instead of the page. The copy keeps everything the page shows, including fonts, images and page sizes it
takes from elsewhere in the PDF. Before the copy is used, PDFStream checks that it looks exactly like the page;
if it does not (this happens on a page with layers that the PDF switches off, because the copy's layers are not
the ones the PDF's layer settings name), the fields are drawn into the page itself instead, which keeps those
layers off the paper. Your file or byte array is never changed, and pages without fields are drawn untouched.

- **Fields print as they print from a PDF viewer.** A field that is not marked for printing (a "screen only"
  field, such as an instruction or a button) stays off the paper.
- **Values come from what the PDF stores.** Each field prints its stored appearance. A field that has a value
  but no stored appearance, and every field of a form that says its appearances must be rebuilt (the form's
  `NeedAppearances` setting, which some form-filling tools set instead of writing the appearance), gets an
  appearance built by PDFium from its value first. No form scripts run, so a value that a script would format
  (a date or currency format, for example) prints as it is stored.
- **XFA forms are not supported.** The PDFium build PDFStream ships is built without XFA. A form made only of
  XFA (a "dynamic" form from Adobe LiveCycle or AEM Designer) has no ordinary fields to print, so only the page
  content prints, which for such forms is often a single "Please wait..." page. A form that carries both kinds
  prints its ordinary fields. Save or print such a form to a plain PDF first.
- If a page's fields cannot be prepared for any reason, the page still prints, without its fields, as earlier
  versions printed it.
- **A page with fields costs a little more.** It is copied, drawn twice at low resolution for the check above
  and read once more, and until the print call returns its copy holds a second copy of what the page uses (its
  fonts and images, for example) in memory. Pages without fields cost nothing extra.
- **Known limitation.** A page that has form fields *and* layers the PDF switches off *and* takes its fonts or
  images from the PDF's page tree instead of carrying them itself can print without those fonts or images
  (the fields are drawn into the page itself, which hides what it takes from the page tree). Such files are
  rare; see [troubleshooting.md](troubleshooting.md#form-fields-are-missing-on-the-paper) for a workaround.
