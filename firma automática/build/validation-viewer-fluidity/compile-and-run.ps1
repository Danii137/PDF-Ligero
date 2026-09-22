# Fluidez del visor: coste de un repintado al navegar y al hacer zoom.
param([string]$PdfReal)
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
    "System.Xml.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)
$referenceArgs = $references | ForEach-Object { "/reference:$_" }

$exe = Join-Path $output "FluidityQa.exe"
$appSources = Get-ChildItem -LiteralPath $root -Filter *.cs |
    Select-Object -ExpandProperty FullName
$sources = $appSources + @(Join-Path $validationDir "FluidityQa.cs")
& $csc /nologo /target:exe /optimize /main:FluidityQa.Program `
    "/out:$exe" $referenceArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba de fluidez del visor."
}

foreach ($dll in @(
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"),
    (Join-Path $packages ("PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256" +
        "\Build\x64\pdfium.dll")))) {
    Copy-Item $dll $output -Force
}

& $exe $PdfReal
if ($LASTEXITCODE -ne 0) {
    throw "La prueba de fluidez del visor ha fallado."
}
