# Mide la capa de texto invisible que deja el OCR con la misma clase que usa
# el subrayador. Necesita un PDF ya pasado por OCR; sin argumento usa el que
# deja validation-ocr\compile-and-run.ps1.
param(
    [string]$PdfConOcr
)

$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$output = Join-Path $validationDir "output"
New-Item -ItemType Directory -Force -Path $output | Out-Null

if (-not $PdfConOcr) {
    $PdfConOcr = Join-Path $root ("build\validation-ocr\output\" +
        "caso OCR rápido con espacios y acentos\" +
        "planos escaneados - Málaga OCR.pdf")
}

if (-not (Test-Path -LiteralPath $PdfConOcr)) {
    throw ("No existe el PDF con OCR: $PdfConOcr. " +
        "Ejecuta antes build\validation-ocr\compile-and-run.ps1.")
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Xml.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$referenceArgs = $references | ForEach-Object { "/reference:$_" }

# PdfTextBlockLocator arrastra media aplicacion por sus tipos de estilo y es
# interno, asi que la prueba se compila junto al programa entero y se elige su
# Main. Mide el codigo real y no una copia.
$exe = Join-Path $output "OcrLayerQa.exe"
$appSources = Get-ChildItem -LiteralPath $root -Filter *.cs |
    Select-Object -ExpandProperty FullName
$sources = $appSources + @(Join-Path $validationDir "OcrLayerQa.cs")
& $csc /nologo /target:exe /optimize /main:OcrLayerQa.Program `
    "/out:$exe" $referenceArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba de la capa OCR."
}

Copy-Item `
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $output -Force
Copy-Item `
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $output -Force

& $exe $PdfConOcr
if ($LASTEXITCODE -ne 0) {
    throw "La capa de texto del OCR no supera la comprobacion."
}
