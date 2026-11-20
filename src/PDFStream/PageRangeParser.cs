using System.Globalization;

namespace PDFStream;

/// <summary>
/// Turns the page-range text of <see cref="PrintOptions.Pages"/> (for example "1-5, 8, 10-12")
/// into the zero-based page indexes the engine uses.
/// </summary>
internal static class PageRangeParser
{
    /// <summary>Tries to turn a page-range text into zero-based page indexes.</summary>
    /// <param name="spec">The text, with pages counted from 1. <see langword="null"/>, empty or blank means every page.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <param name="zeroBasedPages">
    /// The pages to print, in the order and with the repeats written in <paramref name="spec"/>;
    /// empty when the method returns <see langword="false"/>.
    /// </param>
    /// <param name="error">
    /// <see cref="PrintStatus.Ok"/> on success, <see cref="PrintStatus.InvalidPageRange"/> for text
    /// that is not a page range, or <see cref="PrintStatus.PageNotFound"/> for a page the document does not have.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="zeroBasedPages"/> is usable.</returns>
    internal static bool TryParse(string? spec, int pageCount, out IReadOnlyList<int> zeroBasedPages, out PrintStatus error)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            zeroBasedPages = Enumerable.Range(0, Math.Max(pageCount, 0)).ToArray();
            error = PrintStatus.Ok;
            return true;
        }

        // Two passes on purpose. The first checks only the text, so a typo is reported as a typo
        // even when an earlier part names a missing page; the second checks the page numbers
        // against the document. Each range is checked before it is expanded, so "1-2000000000"
        // is rejected without building a two-billion-entry list.
        var ranges = new List<(int First, int Last)>();
        foreach (string part in spec.Split(','))
        {
            if (!TryParsePart(part, out (int First, int Last) range, out bool overflow))
            {
                return Fail(PrintStatus.InvalidPageRange, out zeroBasedPages, out error);
            }

            ranges.Add(overflow ? (int.MaxValue, int.MaxValue) : range);
        }

        foreach ((_, int last) in ranges)
        {
            if (last > pageCount)
            {
                return Fail(PrintStatus.PageNotFound, out zeroBasedPages, out error);
            }
        }

        var pages = new List<int>();
        foreach ((int first, int last) in ranges)
        {
            for (int page = first; page <= last; page++)
            {
                pages.Add(page - 1);
            }
        }

        zeroBasedPages = pages;
        error = PrintStatus.Ok;
        return true;
    }

    /// <summary>
    /// Parses one comma-separated piece: a page ("8") or a range ("10-12"), spaces allowed around
    /// the numbers. Fails for anything else, including empty pieces, 0, signs and reversed ranges.
    /// </summary>
    /// <param name="part">The piece, without the surrounding commas.</param>
    /// <param name="range">The first and last page, 1-based, equal for a single page.</param>
    /// <param name="overflow">
    /// <see langword="true"/> when a number is made of digits but is too large for an int. Such a
    /// page certainly does not exist, so the caller reports it as a missing page, not as a typo.
    /// </param>
    private static bool TryParsePart(string part, out (int First, int Last) range, out bool overflow)
    {
        range = default;
        overflow = false;

        string[] numbers = part.Split('-');
        if (numbers.Length is < 1 or > 2)
        {
            return false;
        }

        if (!TryParsePage(numbers[0], out int first, out bool firstOverflow))
        {
            return false;
        }

        int last = first;
        bool lastOverflow = false;
        if (numbers.Length == 2 && !TryParsePage(numbers[1], out last, out lastOverflow))
        {
            return false;
        }

        overflow = firstOverflow || lastOverflow;
        if (!overflow && last < first)
        {
            return false;
        }

        range = (first, last);
        return true;
    }

    /// <summary>
    /// Parses one page number made only of the digits 0-9 (after trimming spaces). A leading sign,
    /// a decimal point and non-ASCII digits are refused on purpose: the format is the one a user
    /// types into a print dialog, not whatever the current culture accepts.
    /// </summary>
    /// <param name="text">The text of the number.</param>
    /// <param name="page">The page number, 1 or more.</param>
    /// <param name="overflow">Whether the digits were valid but too large for an int.</param>
    private static bool TryParsePage(string text, out int page, out bool overflow)
    {
        page = 0;
        overflow = false;

        string digits = text.Trim();
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out page))
        {
            overflow = true;
            return true;
        }

        // Pages are numbered from 1.
        return page >= 1;
    }

    private static bool Fail(PrintStatus status, out IReadOnlyList<int> zeroBasedPages, out PrintStatus error)
    {
        zeroBasedPages = Array.Empty<int>();
        error = status;
        return false;
    }
}
