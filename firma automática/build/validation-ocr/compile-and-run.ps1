$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$output = Join-Path $validationDir "output"
New-Item -ItemType Directory -Force -Path $output | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$sources = @(
    (Join-Path $root "PdfAtomicFileService.cs"),
    (Join-Path $root "PdfPageOrganizerService.cs"),
    (Join-Path $root "PdfOcrService.cs"),
    (Join-Path $validationDir "OcrServiceQa.cs")
)
$referenceArgs = $references | ForEach-Object { "/reference:$_" }
$exe = Join-Path $output "OcrServiceQa.exe"
& $csc /target:exe /optimize "/out:$exe" $referenceArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba OCR."
}

Copy-Item `
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $output -Force
Copy-Item `
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $output -Force
Copy-Item `
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll") `
    $output -Force
Copy-Item `
    (Join-Path $packages "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll") `
    $output -Force
Copy-Item `
    (Join-Path $root "runtime\ocr") `
    $output -Recurse -Force

& $exe $output
if ($LASTEXITCODE -ne 0) {
    throw "La prueba OCR ha fallado."
}
