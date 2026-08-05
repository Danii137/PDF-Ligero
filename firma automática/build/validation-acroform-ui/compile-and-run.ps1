$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$packages = Join-Path $root "packages"
$output = Join-Path $validation "output"
$run = Join-Path `
    $validation `
    ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
     ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$itext = Join-Path `
    $packages `
    "iTextSharp.5.5.13.3\lib\itextsharp.dll"
$bouncy = Join-Path `
    $packages `
    "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"
$pdfiumViewer = Join-Path `
    $packages `
    "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$pdfiumNative = Join-Path `
    $packages `
    "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll"
$exe = Join-Path $output "AcroFormUiQa.exe"

& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.AcroFormUiQa `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Xml.dll `
    /reference:System.Windows.Forms.dll `
    "/reference:$itext" `
    "/reference:$bouncy" `
    "/reference:$pdfiumViewer" `
    (Join-Path $root "AppBranding.cs") `
    (Join-Path $root "PdfAtomicFileService.cs") `
    (Join-Path $root "PdfAcroFormService.cs") `
    (Join-Path $root "PdfAcroFormFillForm.cs") `
    (Join-Path $validation "AcroFormUiQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar AcroFormUiQa."
}

Copy-Item $itext $output -Force
Copy-Item $bouncy $output -Force
Copy-Item $pdfiumViewer $output -Force
Copy-Item $pdfiumNative $output -Force

& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "AcroFormUiQa ha fallado."
}

Set-Content `
    -LiteralPath (Join-Path $validation "latest-run.txt") `
    -Value $run `
    -Encoding UTF8
Write-Host "QA visual AcroForm: $run"
