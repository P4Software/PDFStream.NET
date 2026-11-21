#Requires -Version 7.0
<#
.SYNOPSIS
    Draws the PDFStream.NET package icon (src/PDFStream.NET/icon.png) from code.

.DESCRIPTION
    The icon is generated, not hand-edited, so that anyone can rebuild exactly the same picture and change it in
    review like any other source file. Run it from anywhere:

        pwsh -File tools/make-icon.ps1

    What it draws, on a 256 x 256 transparent canvas:
      - a rounded tile in P4 Blue (#0A5FAD), so the icon keeps its shape on the white nuget.org page and in the
        light and dark themes of IDE package managers;
      - a white PDF page with a folded corner in P4 Orange (#FBAD48), carrying a downward double chevron (the
        "stream" of pages flowing into the printer);
      - a white printer body in front of the page, with a Deep Navy (#084A8A) paper slot, an orange status light
        and a printed sheet (two blue lines of "text") coming out of the slot.

    Only flat brand colours are used (no gradients, no text, no third-party artwork or fonts), and every shape is
    large enough to survive being shown at 32 px, which is how small NuGet clients draw package icons.

    Quality: the picture is drawn at four times the final size with anti-aliasing and then reduced with
    high-quality bicubic filtering. That gives smoother edges than anti-aliasing at 256 px alone.

    Needs only PowerShell 7 on Windows: System.Drawing ships with it. No Node, NPM or image tools.
#>
[CmdletBinding()]
param(
    # Where the PNG is written. The default is the file the library project packs as the package icon.
    [string] $OutputPath = (Join-Path $PSScriptRoot '..' 'src' 'PDFStream.NET' 'icon.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# Final size in pixels, and the supersampling factor used while drawing.
$size = 256
$scale = 4

# Brand colours (P4 Software brand guidelines). Nothing else is used.
$p4Blue = [System.Drawing.ColorTranslator]::FromHtml('#0A5FAD')
$deepNavy = [System.Drawing.ColorTranslator]::FromHtml('#084A8A')
$p4Orange = [System.Drawing.ColorTranslator]::FromHtml('#FBAD48')
$white = [System.Drawing.Color]::White

<#
    Every coordinate below is written in the 256 px design grid and multiplied by the supersampling factor here,
    so the geometry stays readable.
#>
function ConvertTo-Canvas([double] $value) { [single]($value * $scale) }

# A rectangle with rounded corners, as a closed path. Each corner can have its own radius (0 = square).
function New-RoundedRectanglePath {
    param(
        [double] $X, [double] $Y, [double] $Width, [double] $Height,
        [double] $TopLeft, [double] $TopRight, [double] $BottomRight, [double] $BottomLeft
    )
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $l = ConvertTo-Canvas $X; $t = ConvertTo-Canvas $Y
    $r = ConvertTo-Canvas ($X + $Width); $b = ConvertTo-Canvas ($Y + $Height)
    $corner = { param($radius) [single](2 * (ConvertTo-Canvas $radius)) }

    if ($TopLeft -gt 0) { $d = & $corner $TopLeft; $path.AddArc($l, $t, $d, $d, 180, 90) } else { $path.AddLine($l, $t, $l, $t) }
    if ($TopRight -gt 0) { $d = & $corner $TopRight; $path.AddArc($r - $d, $t, $d, $d, 270, 90) } else { $path.AddLine($r, $t, $r, $t) }
    if ($BottomRight -gt 0) { $d = & $corner $BottomRight; $path.AddArc($r - $d, $b - $d, $d, $d, 0, 90) } else { $path.AddLine($r, $b, $r, $b) }
    if ($BottomLeft -gt 0) { $d = & $corner $BottomLeft; $path.AddArc($l, $b - $d, $d, $d, 90, 90) } else { $path.AddLine($l, $b, $l, $b) }
    $path.CloseFigure()
    return $path
}

# A closed polygon from design-grid points given as pairs: x1, y1, x2, y2, ...
function New-PolygonPath([double[]] $Coordinates) {
    $points = for ($i = 0; $i -lt $Coordinates.Count; $i += 2) {
        [System.Drawing.PointF]::new((ConvertTo-Canvas $Coordinates[$i]), (ConvertTo-Canvas $Coordinates[$i + 1]))
    }
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddPolygon([System.Drawing.PointF[]] $points)
    return $path
}

function Invoke-Fill($graphics, $color, $path) {
    $brush = [System.Drawing.SolidBrush]::new($color)
    try { $graphics.FillPath($brush, $path) } finally { $brush.Dispose(); $path.Dispose() }
}

$big = [System.Drawing.Bitmap]::new($size * $scale, $size * $scale, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($big)
try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 1. The tile: the whole canvas, with generous corners like a modern app icon.
    Invoke-Fill $g $p4Blue (New-RoundedRectanglePath -X 0 -Y 0 -Width 256 -Height 256 -TopLeft 56 -TopRight 56 -BottomRight 56 -BottomLeft 56)

    # 2. The PDF page. Its top-right corner is cut off along a diagonal, and the folded flap fills the cut.
    #    The page runs down behind the printer; the printer's outline (step 4) hides its lower part.
    $pageLeft = 70; $pageRight = 186; $pageTop = 16; $pageBottom = 160; $fold = 36
    Invoke-Fill $g $white (New-PolygonPath @(
            $pageLeft, $pageTop,
            ($pageRight - $fold), $pageTop,
            $pageRight, ($pageTop + $fold),
            $pageRight, $pageBottom,
            $pageLeft, $pageBottom))
    # The flap: a right triangle whose right angle points into the page, like paper folded towards the viewer.
    Invoke-Fill $g $p4Orange (New-PolygonPath @(
            ($pageRight - $fold), $pageTop,
            ($pageRight - $fold), ($pageTop + $fold),
            $pageRight, ($pageTop + $fold)))

    # 3. The stream: two stacked chevrons pointing down, towards the printer. Round caps and joins keep them
    #    legible when the icon is reduced to a few pixels.
    $pen = [System.Drawing.Pen]::new($p4Blue, (ConvertTo-Canvas 14))
    try {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        foreach ($top in 64, 90) {
            $chevron = [System.Drawing.PointF[]] @(
                [System.Drawing.PointF]::new((ConvertTo-Canvas 105), (ConvertTo-Canvas $top)),
                [System.Drawing.PointF]::new((ConvertTo-Canvas 128), (ConvertTo-Canvas ($top + 20))),
                [System.Drawing.PointF]::new((ConvertTo-Canvas 151), (ConvertTo-Canvas $top)))
            $g.DrawLines($pen, $chevron)
        }
    } finally { $pen.Dispose() }

    # 4. The printer. First an outline in the tile colour, slightly larger than the body, which cuts a clean gap
    #    between the page and the printer so the page reads as going in behind it; then the body itself.
    $bodyLeft = 28; $bodyTop = 132; $bodyWidth = 200; $bodyHeight = 72; $bodyRadius = 20; $gap = 9
    Invoke-Fill $g $p4Blue (New-RoundedRectanglePath -X ($bodyLeft - $gap) -Y ($bodyTop - $gap) -Width ($bodyWidth + 2 * $gap) -Height ($bodyHeight + 2 * $gap) -TopLeft ($bodyRadius + $gap) -TopRight ($bodyRadius + $gap) -BottomRight ($bodyRadius + $gap) -BottomLeft ($bodyRadius + $gap))
    Invoke-Fill $g $white (New-RoundedRectanglePath -X $bodyLeft -Y $bodyTop -Width $bodyWidth -Height $bodyHeight -TopLeft $bodyRadius -TopRight $bodyRadius -BottomRight $bodyRadius -BottomLeft $bodyRadius)

    # 5. The printed sheet coming out of the front. A gap in the tile colour around it separates it from the
    #    white body; the slot is drawn last, over the top of the sheet, so the sheet seems to come out of it.
    $sheetLeft = 80; $sheetRight = 176; $sheetTop = 172; $sheetBottom = 236; $sheetGap = 7
    Invoke-Fill $g $p4Blue (New-RoundedRectanglePath -X ($sheetLeft - $sheetGap) -Y $sheetTop -Width ($sheetRight - $sheetLeft + 2 * $sheetGap) -Height ($sheetBottom - $sheetTop + $sheetGap) -TopLeft 0 -TopRight 0 -BottomRight 0 -BottomLeft 0)
    Invoke-Fill $g $white (New-RoundedRectanglePath -X $sheetLeft -Y $sheetTop -Width ($sheetRight - $sheetLeft) -Height ($sheetBottom - $sheetTop) -TopLeft 0 -TopRight 0 -BottomRight 4 -BottomLeft 4)

    # Two lines of "printed" content on the sheet, in the tile blue, so it reads as a page that came out printed.
    Invoke-Fill $g $p4Blue (New-RoundedRectanglePath -X 96 -Y 194 -Width 64 -Height 9 -TopLeft 4.5 -TopRight 4.5 -BottomRight 4.5 -BottomLeft 4.5)
    Invoke-Fill $g $p4Blue (New-RoundedRectanglePath -X 96 -Y 211 -Width 42 -Height 9 -TopLeft 4.5 -TopRight 4.5 -BottomRight 4.5 -BottomLeft 4.5)

    # The paper slot: one bold Deep Navy bar across the front of the printer.
    Invoke-Fill $g $deepNavy (New-RoundedRectanglePath -X 56 -Y 164 -Width 144 -Height 14 -TopLeft 7 -TopRight 7 -BottomRight 7 -BottomLeft 7)

    # The status light, in orange, so the accent colour appears twice and balances the folded corner.
    $light = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $light.AddEllipse((ConvertTo-Canvas 194), (ConvertTo-Canvas 142), (ConvertTo-Canvas 16), (ConvertTo-Canvas 16))
    Invoke-Fill $g $p4Orange $light
}
finally { $g.Dispose() }

# Reduce to the final size with high-quality bicubic filtering. TileFlipXY stops the filter from blending the
# transparent area outside the picture into its edges.
$final = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$fg = [System.Drawing.Graphics]::FromImage($final)
$attributes = [System.Drawing.Imaging.ImageAttributes]::new()
try {
    $fg.Clear([System.Drawing.Color]::Transparent)
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $fg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $fg.DrawImage($big, [System.Drawing.Rectangle]::new(0, 0, $size, $size), 0, 0, $big.Width, $big.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
}
finally { $attributes.Dispose(); $fg.Dispose(); $big.Dispose() }

try {
    $fullPath = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    $final.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $final.Dispose() }

$bytes = (Get-Item $fullPath).Length
"Wrote $fullPath ($size x $size, $bytes bytes)."
# nuget.org accepts icons up to 1 MB; this one should stay far below the 100 KB this project allows itself.
if ($bytes -gt 100KB) { throw "The icon is $bytes bytes, more than the 100 KB budget. Simplify the drawing." }
