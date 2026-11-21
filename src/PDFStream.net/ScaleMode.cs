namespace PDFStream.net;

/// <summary>How each PDF page is sized onto the paper.</summary>
public enum ScaleMode
{
    /// <summary>Shrink or enlarge the page to fit the printable area, centred, rotating it to match the paper when that makes it fit better.</summary>
    FitToPage,

    /// <summary>
    /// Print at the page's own size (100%) with no scaling, centred, rotating it to match the paper when that keeps more of
    /// it on the paper; anything outside the printable area is cut off.
    /// </summary>
    ActualSize,
}
