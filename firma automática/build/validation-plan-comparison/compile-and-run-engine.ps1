$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$packages = Join-Path $root "packages"
$output = Join-Path $validation "output"
$runPointer = Join-Path $validation "latest-run.txt"
if (-not (Test-Path $runPointer)) {
    throw "Ejecuta primero compile-and-run.ps1 para crear los fixtures."
}

$run = (Get-Content -Raw $runPointer).Trim()
$sourceA = Join-Path $run "revision-A.pdf"
$sourceB = Join-Path $run "revision-B.pdf"
if (-not (Test-Path $sourceA) -or -not (Test-Path $sourceB)) {
    throw "La carpeta indicada por latest-run.txt no contiene los fixtures."
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$pdfiumViewer = Join-Path `
    $packages `
    "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$pdfiumNative = Join-Path `
    $packages `
    "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll"
$exe = Join-Path $output "PlanComparisonEngineQa.exe"

& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /platform:x64 `
    /main:FirmaAutomatica.PlanComparisonEngineQa `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    "/reference:$pdfiumViewer" `
    (Join-Path $root "PdfDocumentOpenService.cs") `
    (Join-Path $root "PdfPlanComparisonService.cs") `
    (Join-Path $validation "PlanComparisonEngineQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar PlanComparisonEngineQa."
}

Copy-Item $pdfiumViewer $output -Force
Copy-Item $pdfiumNative $output -Force

Write-Host "RUN_DIRECTORY=$run"
& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "La validación real del motor de comparación ha fallado."
}
