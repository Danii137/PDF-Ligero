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
$pdfiumViewer = Join-Path `
    $packages `
    "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$pdfiumNative = Join-Path `
    $packages `
    "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll"
$exe = Join-Path $output "TextEditUiQa.exe"

& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.TextEditUiQa `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/reference:$pdfiumViewer" `
    (Join-Path $root "AppBranding.cs") `
    (Join-Path $root "PdfTextEditSelectionController.cs") `
    (Join-Path $root "PdfTextEditDialog.cs") `
    (Join-Path $validation "TextEditUiQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar TextEditUiQa."
}

Copy-Item $pdfiumViewer $output -Force
Copy-Item $pdfiumNative $output -Force

$fixture = Join-Path `
    (Split-Path -Parent $validation) `
    "validation-rectangle-zoom\fixture-rectangle-zoom.pdf"
if (-not (Test-Path -LiteralPath $fixture)) {
    throw "Falta el fixture de selección rectangular: $fixture"
}

& $exe $fixture $run
if ($LASTEXITCODE -ne 0) {
    throw "TextEditUiQa ha fallado."
}

Set-Content `
    -LiteralPath (Join-Path $validation "latest-run.txt") `
    -Value $run `
    -Encoding UTF8
Write-Host "QA de edición de texto: $run"
