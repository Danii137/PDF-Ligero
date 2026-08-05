$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$output = Join-Path $validationDir "output"
$run = Join-Path $validationDir "run"
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$sources = @(
    Get-ChildItem -Path $root -Filter *.cs |
        Select-Object -ExpandProperty FullName
)
$sources += Join-Path $validationDir "OcrUiIntegrationQa.cs"
$referenceArgs = $references | ForEach-Object { "/reference:$_" }
$exe = Join-Path $output "OcrUiIntegrationQa.exe"
& $csc `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.OcrUiIntegrationQa `
    "/out:$exe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba OCR de interfaz."
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
Copy-Item (Join-Path $root "runtime\ocr") $output -Recurse -Force

$fixtureRoot = Get-ChildItem `
    (Join-Path $root "build\validation-ocr\output") `
    -Directory |
    Where-Object { $_.Name -like "caso OCR*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $fixtureRoot) {
    throw "Ejecuta antes validation-ocr\compile-and-run.ps1."
}

$fixture = Join-Path $fixtureRoot.FullName "planos escaneados - Málaga.pdf"
& $exe $run $fixture
if ($LASTEXITCODE -ne 0) {
    throw "La prueba OCR de interfaz ha fallado."
}
