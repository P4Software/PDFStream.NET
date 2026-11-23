# Contributing to PDFStream.NET

Thank you for helping. Bug reports, printer and driver reports, documentation fixes and pull requests are all
welcome. Please open an [issue](https://github.com/P4Software/PDFStream.NET/issues) before a large change, so we
can agree on the approach first.

## What you need

- Windows 10 or 11, x64.
- The .NET 10 SDK (it builds both targets, `net8.0-windows` and `net10.0-windows`). Newer SDKs work too.
- The .NET 8 runtime, to run the `net8.0-windows` tests.
- For the 32-bit test runs: the x86 .NET 8 and .NET 10 runtimes.
- *Microsoft Print to PDF* (it ships with Windows 10 and 11) for the real-print tests. Without it those tests,
  and the library tests that need a real printer name, skip themselves and say why.

## Always run commands from the repository root

`global.json` in the repository root switches `dotnet test` to Microsoft Testing Platform. The .NET SDK only
finds that file from the current folder upwards, so run every command below from the root of your clone.

From any other folder, `dotnet test` falls back to the older VSTest mode and fails. With the Testing Platform
options it does not understand `--project`:

```text
MSBUILD : error MSB1001: Unknown switch.
Switch: --project
```

and with a VSTest-style project argument it stops with:

```text
error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later.
```

For the same reason, filter tests with the Testing Platform options (`--filter-method`, `--filter-class`,
`--filter-trait`), never with the VSTest `--filter` option.

## Build and test

```text
dotnet build
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --framework net8.0-windows
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --framework net10.0-windows
```

Run both frameworks before you open a pull request. The sample's tests only exist for `net10.0-windows`,
because the sample targets only that framework.

One test, one class, or one category:

```text
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --framework net10.0-windows --filter-method "PDFStream.NET.Tests.Acceptance.RealPrintTests.Prints_three_page_pdf_to_file_with_three_pages_and_non_blank_output"
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --framework net10.0-windows --filter-class "PDFStream.NET.Tests.PdfPrinterTests"
dotnet test --project tests/PDFStream.NET.Tests --filter-trait "Category=RealPrinter" --framework net8.0-windows
```

32-bit (x86) runs, which load the x86 `pdfium.dll`:

```text
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --arch x86 --framework net8.0-windows
dotnet test --project tests/PDFStream.NET.Tests/PDFStream.NET.Tests.csproj --arch x86 --framework net10.0-windows
```

Arm64 cannot be tested on an x64 machine; it is a manual check on a real Arm64 PC.

The real-print tests print to *Microsoft Print to PDF* into a temporary folder, open the produced PDF again and
count its pages and the ink on each page. A real print must never be judged by its status alone.

Telemetry: to keep the .NET CLI and the Testing Platform from sending usage data, set
`DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` in your environment (CI does).

## Package and sample

```text
dotnet pack src/PDFStream.NET/PDFStream.NET.csproj --configuration Release --output artifacts/package
dotnet publish samples/PDFStream.NET.TestPrint/PDFStream.NET.TestPrint.csproj --framework net10.0-windows --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:PublishDocumentationFiles=false --output artifacts/testprint
```

The package is named `PDFStream.NET`, so `dotnet pack` writes `artifacts/package/PDFStream.NET.<version>.nupkg`
(`PDFStream.NET.1.0.0-rc.2.nupkg` for the current version). The version is set in one place, `VersionPrefix` and
`VersionSuffix` in `Directory.Build.props`, and the package check, `.github/scripts/check-package.ps1`, expects the
same version: change both together when a new version is released. Run the check locally after a pack:

```text
pwsh -File .github/scripts/check-package.ps1 -PackageFolder artifacts/package
```

The package page on nuget.org is `src/PDFStream.NET/README.nuget.md`, packed as `README.md`; the repository's own
`README.md` is the GitHub page and is not packed. Write the package readme for nuget.org: no HTML, only absolute
`https` links, and images only from hosts nuget.org allows (for example `img.shields.io`). Its
`dotnet add package PDFStream.NET --version <version>` line must name the version being released; the package check
fails otherwise. The package icon, `src/PDFStream.NET/icon.png`, is drawn by `tools/make-icon.ps1`
(`pwsh -File tools/make-icon.ps1`): change the script and run it again rather than editing the picture.
The assembly inside it is `PDFStream.NET.dll` and the namespace is `PDFStream.NET`.

The package does not contain `pdfium.dll` itself: it depends on the `bblanchon.PDFium.Win32` package, whose
`runtimes/win-x64`, `win-x86` and `win-arm64` folders give a consuming app the right copy. If you change
anything about packaging, check it from a separate consumer app outside the repository: reference the package
with `<PackageReference Include="PDFStream.NET" Version="<version>" />`, add
`artifacts/package` as a package source in that app's `nuget.config`, point `NUGET_PACKAGES` at an empty
folder (so an older copy of the package in your cache cannot stand in for the new one), build and publish it
with `-r win-x64`, `-r win-x86`, `-r win-arm64` and with no runtime identifier, for `net8.0-windows` and
`net10.0-windows`, and check that the `pdfium.dll` next to the exe is the one for that architecture. CI checks
the package's own contents (licence, notices, package readme, icon, metadata, the PDFium dependency) on every run.

The publish command makes one exe that includes .NET and `pdfium.dll`, so it runs by double-click on a
machine without .NET.

## Before a release

Work through the [manual release checklist](docs/release-checklist.md). Nothing in it counts as verified until
someone has run the check and written the machine, the date and the result in the row. Never mark a row from
memory.

## Releasing

Releases are published to nuget.org by the **Publish to NuGet** workflow (`.github/workflows/publish.yml`) with
NuGet Trusted Publishing: GitHub gives the workflow a short-lived token, nuget.org exchanges it for an API key that
is valid for one hour, and the workflow pushes with it. No API key is stored anywhere, so there is none to rotate
or leak. The workflow runs only when started by hand, only from `main`, and only after a reviewer approves it.

One-time set-up (the owner):

1. On nuget.org, open your name menu, choose **Trusted Publishing** and add a policy: Repository Owner
   `P4Software`, Repository `PDFStream.NET`, Workflow File `publish.yml` (the file name only), Environment
   `release`, with the scope to push new versions of the package `PDFStream.NET`.
2. On GitHub, in the repository settings, create the environment `release` with a required reviewer.
3. Add the repository secret `NUGET_USER` with the nuget.org profile name (not an e-mail address) as its value.

If nuget.org shows the new policy as temporarily active (it does this when it cannot yet look up the repository's
IDs, usually for private repositories), it stays active for 7 days and becomes permanently active with the first
successful publish.

Each release:

1. Set the version: `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`, the pinned version in
   `.github/scripts/check-package.ps1`, and the `--version` install line in `src/PDFStream.NET/README.nuget.md`.
   Add the version's section to `CHANGELOG.md` and update `PackageReleaseNotes` in the library's project file.
2. Merge to `main` once CI is green.
3. On GitHub, open **Actions**, choose **Publish to NuGet**, then **Run workflow** with `main` selected.
4. Approve the run when GitHub asks (the `release` environment). The workflow builds with warnings as errors,
   runs both test suites, packs, runs the package check and only then pushes.
5. Wait while nuget.org shows the new version as **Validating**; it can be installed once that is done, usually
   within some minutes. A version on nuget.org can never be changed or reused: a mistake means a new version.

Re-running the workflow for a version that is already on nuget.org pushes nothing (`--skip-duplicate`).

## Rules for code

- The public API never throws for bad input or printer problems: it returns a `PrintResult` with a status and
  a plain-language message that says what happened and what to do next.
- Every public type and member has XML documentation (a missing one fails the build), and non-obvious code
  has a comment saying why.
- "Printed" means pages were rendered and sent. Tests check output pixels, not only the status.
- PDFium is not thread-safe: every call into it goes through the one process-wide lock in `PdfiumLibrary`.
- No telemetry, network calls, licence keys, watermarks or expiry dates, ever.
- Every dependency must be permissively licensed (MIT, BSD, Apache-2.0) and redistributable. Read its licence
  file before you add it, and add it to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). PDFsharp in the test
  project is pinned to 6.2.4 on purpose: later versions are AGPL.

## Commits and pull requests

- Commits are authored by you, under your own name and e-mail address.
- Never commit secrets: no keys, passwords, tokens, certificates or private settings files.
- Do not copy code from commercial PDF libraries or from private applications.
- By contributing you agree that your contribution is released under the project's [MIT licence](LICENSE).
