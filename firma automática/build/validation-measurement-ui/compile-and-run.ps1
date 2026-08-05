$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$packages = Join-Path $root "packages"
$output = Join-Path $validation "output"
$runName = "run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
    ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$run = Join-Path $validation $runName

New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $output "MeasurementUiQa.exe"
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
    (Join-Path $root "PdfMeasurementModel.cs"),
    (Join-Path $root "PdfMeasurementController.cs"),
    (Join-Path $validation "MeasurementUiQa.cs")
)
$referenceArgs = $references | ForEach-Object { "/reference:$_" }

& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.MeasurementUiQa `
    "/out:$exe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la validación UI de medición."
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

Write-Host "RUN_DIRECTORY=$run"
& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "La validación UI de medición ha fallado."
}

Set-Content `
    -LiteralPath (Join-Path $validation "latest-run.txt") `
    -Value $run `
    -Encoding UTF8

Write-Host "UI de medición validada correctamente."
