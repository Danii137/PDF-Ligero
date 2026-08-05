$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$output = Join-Path $validation "output"
$runName = "run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
    ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$run = Join-Path $validation $runName

New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $output "MeasurementEngineQa.exe"
$model = Join-Path $root "PdfMeasurementModel.cs"
$harness = Join-Path $validation "MeasurementEngineQa.cs"

& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.MeasurementEngineQa `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    $model `
    $harness
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar el motor de medición."
}

Write-Host "RUN_DIRECTORY=$run"
& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "La validación del motor de medición ha fallado."
}

Set-Content `
    -Path (Join-Path $validation "latest-run.txt") `
    -Value $run `
    -Encoding UTF8

Write-Host "Motor de medición validado correctamente."
