$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$fixtureValidation = Join-Path `
    (Split-Path -Parent $validationDir) `
    "validation-bookmarks"
$output = Join-Path $validationDir "output"
$run = Join-Path $validationDir "run"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& (Join-Path $fixtureValidation "compile-and-run.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo preparar el fixture de marcadores."
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$referenceArgs = $references |
    ForEach-Object { "/reference:$_" }
$sources = @(
    Get-ChildItem -LiteralPath $root -Filter *.cs |
        Select-Object -ExpandProperty FullName
)
$sources += Join-Path `
    $validationDir `
    "BookmarkViewerIntegrationQa.cs"
$exe = Join-Path `
    $output `
    "BookmarkViewerIntegrationQa.exe"

& $csc `
    /target:exe `
    /optimize+ `
    /main:FirmaAutomatica.BookmarkViewerIntegrationQa `
    "/out:$exe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar el recorrido real de marcadores."
}

Copy-Item `
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $output `
    -Force
Copy-Item `
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $output `
    -Force
Copy-Item `
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll") `
    $output `
    -Force
Copy-Item `
    (Join-Path $packages "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll") `
    $output `
    -Force

$fixture = Join-Path `
    (Join-Path $fixtureValidation "run") `
    "bookmark-advanced-fixture.pdf"
& $exe $run $fixture
exit $LASTEXITCODE
