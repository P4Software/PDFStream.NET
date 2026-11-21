# PDFStream release checklist

Run these checks before each release. Rows marked "Not run" have not been done: nothing here counts as
verified until someone has run it, written the machine, the date, the result and their initials in the row,
and (for a failure) opened an issue. Never change a result to Pass from memory.

How to read the result column: **Pass**, **Fail** (write what happened), **Not run**.

## 1. Automated checks (run by the test suite)

The real-print acceptance tests print through *Microsoft Print to PDF* into a temporary file, re-open the
produced PDF with the library's own engine and check the page count and that no page is blank. Run them with:

```
dotnet test --project tests/PDFStream.Tests --filter-trait "Category=RealPrinter"
dotnet test --project tests/PDFStream.Tests --filter-trait "Category=RealPrinter" -f net10.0-windows --arch x86
```

A machine without the printer skips them and says why. They do not replace the manual checks in section 2:
Print to PDF has no paper, no tray, no duplex unit and no queue that can go offline.

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| Real-print tests, x64 host, net10.0-windows | Windows 11 x64 developer workstation | 2026-11-20 | Pass (7 of 7), including the vector-path check | |
| Real-print tests, x86 (32-bit) host, net10.0-windows | Windows 11 x64 developer workstation | 2026-11-20 | Pass (7 of 7), including the vector-path check | |
| Real-print tests, x64 host, net8.0-windows | Windows 11 x64 developer workstation | 2026-11-20 | Pass (7 of 7) with `dotnet test --project tests/PDFStream.Tests --filter-trait "Category=RealPrinter" --framework net8.0-windows`, and in the full net8.0-windows suite (288 of 288) | |
| Real-print tests, x86 (32-bit) host, net8.0-windows | Windows 11 x64 developer workstation | 2026-11-20 | Pass, in the full suite with `--arch x86 --framework net8.0-windows` (288 of 288, the 7 real-print tests included) | |

What the automated tests cover: three pages in, three pages out and none blank; five pages of mixed sizes and
orientations; a page range; two software copies (Print to PDF reports a maximum of 1 copy); a password-protected
PDF with the right password; a wrong password returning PasswordInvalid and writing no file; and that
Print to PDF received the pages as drawing commands on the device context (vector path), not as bitmaps.

Known behaviour found while writing them: when the print call returns, *Microsoft Print to PDF* may not have
finished writing the output file yet (a read straight afterwards failed with "file in use" in the 32-bit
host). The tests wait for the file to be complete. A program that opens the output file right after printing
must do the same.

## 2. Manual checks

Each check needs real hardware or a real Windows setup that the test suite cannot reproduce.

### 2.1 Processor architectures

Print a multi-page PDF with the sample or your own program built for each architecture and look at the paper
(or the produced file): all pages, right orientation, nothing cut off.

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| Print on x86 (32-bit) Windows or a 32-bit process on a physical printer | | | Not run | |
| Print on arm64 Windows (a real arm64 PC, not an emulator of x64) | | | Not run | |

### 2.2 Where the program runs

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| Run from a Windows service under a normal service account (not LocalSystem, not an administrator), printing to a printer that account can use | | | Not run | |
| Run from a Task Scheduler task ("Run whether user is logged on or not") | | | Not run | |
| Run from an IIS application pool: an ASP.NET Core app printing to a printer visible to the app pool identity | | | Not run | |

### 2.3 Printers and paper

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| A physical printer: a multi-page PDF comes out complete, correctly scaled and in the right orientation | | | Not run | |
| A physical duplex printer: `DuplexMode.LongEdge` and `DuplexMode.ShortEdge` give the right binding edge on a 4-page PDF | | | Not run | |
| A 5-page PDF with mixed sizes and orientations (A4 portrait and Letter landscape alternating): every page fits the sheet, landscape pages are turned, nothing is cut off | | | Not run | |
| A network printer that Windows lists as Offline (the status comes from SNMP) but that is on and reachable still prints | | | Not run | |
| Duplex with software copies (`Copies` above what the driver supports) and a PDF with an odd page count: the copies are collated | | | Not run | |

Known limitation for the duplex row: with software copies and an odd page count, the next copy starts on
the back of the last sheet of the previous copy. Record in the row that this was seen, and do not mark the
check as Fail for it.

### 2.4 Printer problems return a result, never an exception

Each of these must return `PrintStatus.PrinterUnavailable` with the matching plain-language message and must
not throw or hang.

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| A paused print queue: the message says the printer is paused and to resume it | | | Not run | |
| The Print Spooler service stopped: the message says the Print Spooler is not running and to start it | | | Not run | |

## 3. Vector path on other drivers

The automated test confirms that *Microsoft Print to PDF* gets vector drawing commands and not the bitmap
fallback. The page painter has no public or internal switch that reports which path was used, and none was
added. For a physical printer this can only be judged from the size of the spool file and the print quality
of small text and barcodes.

| Check | Machine | Date | Result | Initials |
| --- | --- | --- | --- | --- |
| Physical printer: small text and barcodes are sharp, and the spool file is small (vector path, not a large picture per page) | | | Not run | |
