$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent (Split-Path -Parent $validationDir)
$packagesDir = Join-Path $sourceRoot "packages"
$outputExe = Join-Path $validationDir "OrganizerUiQa.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$sources = @(
    Get-ChildItem -LiteralPath $sourceRoot -Filter *.cs |
        Select-Object -ExpandProperty FullName
)
$sources += Join-Path $validationDir "OrganizerUiQa.cs"

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$referenceArgs = $references |
    ForEach-Object { "/reference:$_" }

& $csc `
    "/target:exe" `
    "/optimize+" `
    "/main:FirmaAutomatica.OrganizerUiQa" `
    "/out:$outputExe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "La compilación del harness ha fallado."
}

Copy-Item `
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $validationDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $validationDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll") `
    $validationDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll") `
    $validationDir `
    -Force

& $outputExe
exit $LASTEXITCODE
