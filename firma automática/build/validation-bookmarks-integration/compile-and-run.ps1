$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent (Split-Path -Parent $validationDir)
$packagesDir = Join-Path $sourceRoot "packages"
$fixtureValidationDir = Join-Path `
    (Split-Path -Parent $validationDir) `
    "validation-bookmarks"
$outputDir = Join-Path $validationDir "output"
$runDir = Join-Path $validationDir "run"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& (Join-Path $fixtureValidationDir "compile-and-run.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo preparar el fixture avanzado."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$referenceArgs = $references |
    ForEach-Object { "/reference:$_" }
$sources = @(
    (Join-Path $sourceRoot "PdfBookmarkService.cs"),
    (Join-Path $sourceRoot "PdfEditSession.cs"),
    (Join-Path $sourceRoot "PdfMergeService.cs"),
    (Join-Path $validationDir "BookmarkServiceIntegrationQa.cs")
)
$outputExe = Join-Path `
    $outputDir `
    "BookmarkServiceIntegrationQa.exe"

& $csc `
    "/target:exe" `
    "/optimize+" `
    "/main:FirmaAutomatica.BookmarkServiceIntegrationQa" `
    "/out:$outputExe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "La compilación de integración de marcadores ha fallado."
}

Copy-Item `
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $outputDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $outputDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll") `
    $outputDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll") `
    $outputDir `
    -Force

& $outputExe `
    $runDir `
    (Join-Path $fixtureValidationDir "run")
exit $LASTEXITCODE
