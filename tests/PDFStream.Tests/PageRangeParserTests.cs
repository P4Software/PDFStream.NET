using Xunit;

namespace PDFStream.Tests;

/// <summary>Tests <see cref="PageRangeParser"/>: the 1-based page-range text becomes zero-based page indexes.</summary>
public sealed class PageRangeParserTests
{
    /// <summary>Null, empty and blank text all mean "print every page".</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_means_all_pages(string? spec)
    {
        bool ok = PageRangeParser.TryParse(spec, 4, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.True(ok);
        Assert.Equal(PrintStatus.Ok, error);
        Assert.Equal(new[] { 0, 1, 2, 3 }, pages);
    }

    /// <summary>The example from the public documentation: ranges and single pages, in the order written.</summary>
    [Fact]
    public void Parses_1_to_5_8_10_to_12_in_order()
    {
        bool ok = PageRangeParser.TryParse("1-5, 8, 10-12", 12, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.True(ok);
        Assert.Equal(PrintStatus.Ok, error);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 7, 9, 10, 11 }, pages);
    }

    /// <summary>The order and the repeats written by the caller are kept, so "3, 1, 3" prints page 3, 1, 3.</summary>
    [Fact]
    public void Keeps_order_and_duplicates_as_written()
    {
        bool ok = PageRangeParser.TryParse("3, 1, 3, 2-3", 5, out IReadOnlyList<int> pages, out _);

        Assert.True(ok);
        Assert.Equal(new[] { 2, 0, 2, 1, 2 }, pages);
    }

    /// <summary>Spaces around numbers, commas and dashes are ignored; a single-page range is allowed.</summary>
    [Fact]
    public void Tolerates_spaces_and_single_page_ranges()
    {
        bool ok = PageRangeParser.TryParse("  2 - 2 ,4  ", 5, out IReadOnlyList<int> pages, out _);

        Assert.True(ok);
        Assert.Equal(new[] { 1, 3 }, pages);
    }

    /// <summary>A range that runs backwards is a typing mistake, not "pages 2 to 5 in reverse".</summary>
    [Fact]
    public void Reversed_range_5_to_2_is_InvalidPageRange()
    {
        bool ok = PageRangeParser.TryParse("5-2", 10, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.False(ok);
        Assert.Equal(PrintStatus.InvalidPageRange, error);
        Assert.Empty(pages);
    }

    /// <summary>Pages are numbered from 1, so 0 and negative numbers cannot be pages.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0-3")]
    [InlineData("2, 0")]
    public void Zero_or_negative_is_InvalidPageRange(string spec)
    {
        bool ok = PageRangeParser.TryParse(spec, 10, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.False(ok);
        Assert.Equal(PrintStatus.InvalidPageRange, error);
        Assert.Empty(pages);
    }

    /// <summary>A well-formed page that the document does not have is reported as PageNotFound.</summary>
    [Theory]
    [InlineData("4", 3)]
    [InlineData("1-4", 3)]
    [InlineData("1, 2, 99", 3)]
    [InlineData("1-2000000000", 3)]
    [InlineData("99999999999", 3)]
    [InlineData("1", 0)]
    public void Page_beyond_count_is_PageNotFound(string spec, int pageCount)
    {
        bool ok = PageRangeParser.TryParse(spec, pageCount, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.False(ok);
        Assert.Equal(PrintStatus.PageNotFound, error);
        Assert.Empty(pages);
    }

    /// <summary>The last page itself is fine.</summary>
    [Fact]
    public void Last_page_is_accepted()
    {
        bool ok = PageRangeParser.TryParse("3", 3, out IReadOnlyList<int> pages, out _);

        Assert.True(ok);
        Assert.Equal(new[] { 2 }, pages);
    }

    /// <summary>Anything that is not numbers, commas and dashes is a mistake in the text.</summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("1-x")]
    [InlineData("1,,2")]
    [InlineData("1,")]
    [InlineData(",1")]
    [InlineData("1-")]
    [InlineData("1-2-3")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("1 2")]
    [InlineData("all")]
    public void Non_numeric_is_InvalidPageRange(string spec)
    {
        bool ok = PageRangeParser.TryParse(spec, 10, out IReadOnlyList<int> pages, out PrintStatus error);

        Assert.False(ok);
        Assert.Equal(PrintStatus.InvalidPageRange, error);
        Assert.Empty(pages);
    }

    /// <summary>
    /// When the text is both malformed and mentions a missing page, the malformed text is reported:
    /// the caller has to fix the text before the page numbers mean anything.
    /// </summary>
    [Fact]
    public void Malformed_text_wins_over_missing_page()
    {
        bool ok = PageRangeParser.TryParse("99, abc", 3, out _, out PrintStatus error);

        Assert.False(ok);
        Assert.Equal(PrintStatus.InvalidPageRange, error);
    }
}
