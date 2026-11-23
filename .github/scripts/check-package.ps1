#Requires -Version 7.0
<#
.SYNOPSIS
    Checks the PDFStream.NET NuGet package before it is kept as a CI artifact or published to nuget.org.

.DESCRIPTION
    Both workflows run this one script: .github/workflows/ci.yml on every push and pull request, and
    .github/workflows/publish.yml right before it pushes to nuget.org. A package can therefore only be published
    if it passes exactly the checks CI runs.

    It fails (exit code 1, with one "::error" line per problem, which GitHub Actions shows as annotations) when the
    package:
      - is not exactly one PDFStream.NET.<expected version>.nupkg in the folder;
      - lacks the licence, the third-party notices, the package readme, the icon or either library assembly;
      - has the wrong id, title, version, licence, readme, icon, authors or repository in its nuspec, no
        release notes, or no PDFium dependency at the tested version;
      - carries the repository README instead of the package readme (raw HTML, a link or image that is not
        absolute https, or an install command for another version);
      - carries an icon that is not a square PNG of at least 128 px and at most 1 MB (nuget.org's limit);
      - picked up a file type that has no business in it (keys, certificates, settings, scripts).

    Run it locally from the repository root after "dotnet pack ... --output <folder>":

        pwsh -File .github/scripts/check-package.ps1 -PackageFolder <folder>
#>
[CmdletBinding()]
param(
    # The folder dotnet pack wrote the package into.
    [string] $PackageFolder = 'artifacts/package'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The version being released: VersionPrefix plus VersionSuffix in Directory.Build.props. A version published on
# nuget.org is permanent, so the check pins it exactly. Change it together with that file when a new version is
# released; this is the only copy of the pin.
$expectedVersion = '1.0.0-rc.2'
$expectedId = 'PDFStream.NET'
$expectedRepository = 'https://github.com/P4Software/PDFStream.NET'

# The package id, the assembly and the namespace are all PDFStream.NET, so the file is
# PDFStream.NET.<version>.nupkg and its manifest PDFStream.NET.nuspec. Exactly one must exist.
$packages = @(Get-ChildItem -Path $PackageFolder -Filter "$expectedId.*.nupkg" | Where-Object { $_.Name -notlike '*.snupkg' })
if ($packages.Count -ne 1) { throw "Expected one $expectedId.$expectedVersion.nupkg in '$PackageFolder', found $($packages.Count): $($packages.Name -join ', ')" }
if ($packages[0].Name -cne "$expectedId.$expectedVersion.nupkg") { throw "The package file is '$($packages[0].Name)', not $expectedId.$expectedVersion.nupkg" }
$package = $packages[0]

# Reads one entry of the package as text, or as bytes; $null when the entry is missing.
function Read-EntryText($zip, [string] $name) {
    $entry = $zip.Entries | Where-Object { $_.FullName -ceq $name }
    if (-not $entry) { return $null }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Read-EntryBytes($zip, [string] $name) {
    $entry = $zip.Entries | Where-Object { $_.FullName -ceq $name }
    if (-not $entry) { return $null }
    $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return , $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
}

$zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    $nuspecText = Read-EntryText $zip "$expectedId.nuspec"
    if ($null -eq $nuspecText) { throw "The package has no $expectedId.nuspec." }
    [xml] $nuspec = $nuspecText
    $readme = Read-EntryText $zip 'README.md'
    $icon = Read-EntryBytes $zip 'icon.png'
} finally { $zip.Dispose() }

$problems = @()
foreach ($required in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md', 'icon.png', "lib/net8.0-windows7.0/$expectedId.dll", "lib/net10.0-windows7.0/$expectedId.dll") {
    if ($entries -cnotcontains $required) { $problems += "missing file: $required" }
}

# The nuspec metadata nuget.org shows on the package page.
$metadata = $nuspec.package.metadata
# Reads an optional nuspec element as text: missing elements give an empty string instead of a strict-mode error.
function Get-MetadataText([string] $name) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    if ($null -eq $node) { return '' }
    return $node.InnerText
}
if ((Get-MetadataText 'id') -cne $expectedId) { $problems += "the package id is '$(Get-MetadataText 'id')', not $expectedId" }
if ((Get-MetadataText 'title') -cne $expectedId) { $problems += "the package title is '$(Get-MetadataText 'title')', not $expectedId" }
if ((Get-MetadataText 'version') -cne $expectedVersion) { $problems += "the package version is '$(Get-MetadataText 'version')', not $expectedVersion" }
if ((Get-MetadataText 'license') -ne 'MIT') { $problems += 'the licence expression is not MIT' }
if ((Get-MetadataText 'readme') -cne 'README.md') { $problems += 'the package readme is not README.md' }
if ((Get-MetadataText 'icon') -cne 'icon.png') { $problems += 'the nuspec has no <icon>icon.png</icon>' }
if ((Get-MetadataText 'authors') -ne 'P4 Software') { $problems += 'the authors are not P4 Software' }
if ([string]::IsNullOrWhiteSpace((Get-MetadataText 'releaseNotes'))) { $problems += 'the package has no release notes' }
$repository = $metadata.SelectSingleNode("*[local-name()='repository']")
if ($null -eq $repository -or $repository.GetAttribute('url') -cne $expectedRepository) { $problems += 'the repository URL is missing or wrong' }
$groups = @($metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']"))
if ($groups.Count -eq 0) { $problems += 'the package has no dependency groups' }
foreach ($group in $groups) {
    $pdfium = @($group.SelectNodes("*[local-name()='dependency'][@id='bblanchon.PDFium.Win32']"))
    if ($pdfium.Count -ne 1) { $problems += "$($group.GetAttribute('targetFramework')) does not depend on bblanchon.PDFium.Win32 (pdfium.dll)" }
    # The exact PDFium build that was tested (the nuspec writes it as "160.0.8165", which NuGet reads as "160.0.8165 or newer").
    elseif ($pdfium[0].GetAttribute('version') -cne '160.0.8165') { $problems += "$($group.GetAttribute('targetFramework')) depends on bblanchon.PDFium.Win32 $($pdfium[0].GetAttribute('version')), not 160.0.8165" }
}

# The package readme must be the one written for nuget.org (src/PDFStream.NET/README.nuget.md), not the GitHub
# README: nuget.org strips raw HTML, cannot follow relative links and shows images only over https from hosts it
# trusts. Its install command must name the version being released.
if ($null -ne $readme) {
    if ($readme -match '(?i)<\s*/?\s*(div|p|img|br|a|table|span|picture|source|details|summary|h[1-6])\b') { $problems += 'README.md in the package contains raw HTML; it must be the package readme (README.nuget.md)' }
    $targets = @([regex]::Matches($readme, '\]\(\s*([^)\s]+)') | ForEach-Object { $_.Groups[1].Value })
    $notHttps = @($targets | Where-Object { $_ -notmatch '^https://' })
    if ($notHttps.Count -gt 0) { $problems += "README.md in the package has links or images that are not absolute https: $($notHttps -join ', ')" }
    if (-not $readme.Contains("dotnet add package $expectedId --version $expectedVersion")) { $problems += "README.md in the package does not show 'dotnet add package $expectedId --version $expectedVersion'" }
}

# The icon: a PNG (signature check), square, at least 128 px (nuget.org recommends 128 x 128), at most 1 MB.
if ($null -ne $icon) {
    $pngSignature = [byte[]] (0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    if ($icon.Length -lt 24 -or -not [Linq.Enumerable]::SequenceEqual([byte[]] $icon[0..7], $pngSignature)) { $problems += 'icon.png is not a PNG file' }
    else {
        # Width and height are the big-endian 32-bit numbers at bytes 16 and 20 (the IHDR chunk).
        $width = ([int] $icon[16] -shl 24) -bor ([int] $icon[17] -shl 16) -bor ([int] $icon[18] -shl 8) -bor [int] $icon[19]
        $height = ([int] $icon[20] -shl 24) -bor ([int] $icon[21] -shl 16) -bor ([int] $icon[22] -shl 8) -bor [int] $icon[23]
        if ($width -ne $height -or $width -lt 128) { $problems += "icon.png is $width x $height; it must be square and at least 128 px" }
    }
    if ($icon.Length -gt 1MB) { $problems += "icon.png is $($icon.Length) bytes; nuget.org accepts at most 1 MB" }
}

$unexpected = @($entries | Where-Object { $_ -match '\.(snk|pfx|p12|pem|key|cer|ps1|cmd|bat|json|config)$' })
if ($unexpected.Count -gt 0) { $problems += "unexpected files: $($unexpected -join ', ')" }

"Package: $($package.Name)"; $entries | Sort-Object | ForEach-Object { "  $_" }
if ($problems.Count -gt 0) { $problems | ForEach-Object { "::error title=Package check::$_" }; exit 1 }
"The $expectedId package has the licence, the third-party notices, the package readme, the icon, the metadata and the PDFium dependency."
exit 0
