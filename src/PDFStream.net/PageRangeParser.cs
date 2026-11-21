using System.Globalization;

namespace PDFStream.net;

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
    /// page certainly does not exist, so the caller reports it as a missing page, not as a typo (a
    /// reversed range is still a typo, whatever the size of the numbers).
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

        if (!TryParsePage(numbers[0], out int first, out bool firstOverflow, out string firstDigits))
        {
            return false;
        }

        int last = first;
        bool lastOverflow = firstOverflow;
        string lastDigits = firstDigits;
        if (numbers.Length == 2 && !TryParsePage(numbers[1], out last, out lastOverflow, out lastDigits))
        {
            return false;
        }

        overflow = firstOverflow || lastOverflow;

        // A reversed range is a typo whatever the size of the numbers, so the check runs when a bound
        // overflows too; only then the digits are compared, because an int cannot hold the value.
        bool reversed = overflow ? CompareDigits(firstDigits, lastDigits) > 0 : last < first;
        if (reversed)
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
    /// <param name="digits">The digits without spaces and leading zeros, kept so two numbers too large for an int can still be compared.</param>
    private static bool TryParsePage(string text, out int page, out bool overflow, out string digits)
    {
        page = 0;
        overflow = false;

        string trimmed = text.Trim();
        digits = trimmed;
        if (trimmed.Length == 0 || !trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        // "0007" is page 7; stripping the zeros also makes the length a valid measure of size.
        digits = trimmed.TrimStart('0');
        if (digits.Length == 0)
        {
            // Only zeros: page 0, which does not exist.
            return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out page))
        {
            overflow = true;
        }

        return true;
    }

    /// <summary>
    /// Compares two numbers written as digits without leading zeros, with no limit on their size:
    /// the longer one is larger, and equal lengths compare digit by digit.
    /// </summary>
    /// <returns>A positive number when <paramref name="a"/> is larger, negative when smaller, 0 when equal.</returns>
    private static int CompareDigits(string a, string b) =>
        a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);

    private static bool Fail(PrintStatus status, out IReadOnlyList<int> zeroBasedPages, out PrintStatus error)
    {
        zeroBasedPages = Array.Empty<int>();
        error = status;
        return false;
    }
}
