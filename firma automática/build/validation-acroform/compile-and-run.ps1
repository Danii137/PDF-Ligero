$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packages = Join-Path $root "packages"
$output = Join-Path $PSScriptRoot "output"
New-Item -ItemType Directory -Force -Path $output | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$itext = Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"
$bouncy = Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"
$exe = Join-Path $output "AcroFormEngineQa.exe"

& $csc `
    /target:exe `
    /optimize `
    "/out:$exe" `
    "/reference:System.dll" `
    "/reference:System.Core.dll" `
    "/reference:System.Drawing.dll" `
    "/reference:System.Xml.dll" `
    "/reference:System.Windows.Forms.dll" `
    "/reference:$itext" `
    "/reference:$bouncy" `
    (Join-Path $root "AppBranding.cs") `
    (Join-Path $root "PdfAtomicFileService.cs") `
    (Join-Path $root "PdfEditSession.cs") `
    (Join-Path $root "PdfAcroFormService.cs") `
    (Join-Path $root "PdfAcroFormFillForm.cs") `
    (Join-Path $PSScriptRoot "AcroFormEngineQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar AcroFormEngineQa."
}

Copy-Item $itext $output -Force
Copy-Item $bouncy $output -Force
& $exe
if ($LASTEXITCODE -ne 0) {
    throw "AcroFormEngineQa ha fallado."
}
