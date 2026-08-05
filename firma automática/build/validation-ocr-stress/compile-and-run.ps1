$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$output = Join-Path $validationDir "output"
$runName = "run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
    ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$run = Join-Path $validationDir $runName
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Management.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$sources = @(
    (Join-Path $root "PdfAtomicFileService.cs"),
    (Join-Path $root "PdfPageOrganizerService.cs"),
    (Join-Path $root "PdfDocumentOpenService.cs"),
    (Join-Path $root "PdfOcrService.cs"),
    (Join-Path $validationDir "OcrStressQa.cs")
)
$referenceArgs = $references |
    ForEach-Object { "/reference:$_" }
$exe = Join-Path $output "OcrStressQa.exe"
& $csc /target:exe /optimize /platform:x64 `
    "/out:$exe" $referenceArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba OCR stress."
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

Write-Host "RUN_DIRECTORY=$run"
& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "La prueba OCR stress ha detectado fallos. Informe: $run\qa-report.txt"
}
