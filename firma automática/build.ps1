param(
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagesDir = Join-Path $root "packages"
$buildDir = Join-Path $root "build"
$outputDir = Join-Path $buildDir "output"
$nugetExe = Join-Path $packagesDir "nuget.exe"
$iconScript = Join-Path $root "generate-icon.ps1"
$iconSourcePng = Join-Path $root "assets\PDFLigero.png"
$iconPath = Join-Path $outputDir "PDFLigero.ico"
$legacyIconPath = Join-Path $outputDir "FirmaAutomatica.ico"
$ocrRuntimeDir = Join-Path $root "runtime\ocr"
$ocrOutputDir = Join-Path $outputDir "ocr"

New-Item -ItemType Directory -Force -Path $packagesDir | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (-not $SkipRestore) {
    if (-not (Test-Path $nugetExe)) {
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetExe
    }

    & $nugetExe install iTextSharp -Version 5.5.13.3 -OutputDirectory $packagesDir
    & $nugetExe install BouncyCastle -Version 1.8.9 -OutputDirectory $packagesDir
    & $nugetExe install PdfiumViewer -Version 2.13.0 -OutputDirectory $packagesDir
    & $nugetExe install PdfiumViewer.Native.x86_64.v8-xfa -Version 2018.4.8.256 -OutputDirectory $packagesDir
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sources = Get-ChildItem -Path $root -Filter *.cs | Select-Object -ExpandProperty FullName
$outputExe = Join-Path $outputDir "PDFLigero.exe"
$legacyOutputExe = Join-Path $outputDir "FirmaAutomatica.exe"

if (-not (Test-Path $iconScript)) {
    throw "No existe el generador de iconos: $iconScript"
}

if (-not (Test-Path $iconSourcePng)) {
    throw "No existe el icono de PDF Ligero: $iconSourcePng"
}

& $iconScript -InputPngPath $iconSourcePng -OutputIcoPath $iconPath
Copy-Item $iconPath $legacyIconPath -Force

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Xml.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"),
    (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll")
)

$referenceArgs = $references | ForEach-Object { "/reference:$_" }
$compilerArgs = @("/target:winexe", "/optimize", "/out:$outputExe")
if (Test-Path $iconPath) {
    $compilerArgs += "/win32icon:$iconPath"
}

& $csc $compilerArgs $referenceArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "La compilación ha fallado."
}

Copy-Item $outputExe $legacyOutputExe -Force
Copy-Item (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll") $outputDir -Force
Copy-Item (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") $outputDir -Force
Copy-Item (Join-Path $packagesDir "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll") $outputDir -Force
Copy-Item (Join-Path $packagesDir "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll") $outputDir -Force
Copy-Item $iconSourcePng (Join-Path $outputDir "PDFLigero.png") -Force

$requiredOcrFiles = @(
    (Join-Path $ocrRuntimeDir "tesseract.exe"),
    (Join-Path $ocrRuntimeDir "tessdata\spa.traineddata"),
    (Join-Path $ocrRuntimeDir "tessdata\eng.traineddata"),
    (Join-Path $ocrRuntimeDir "tessdata\osd.traineddata"),
    (Join-Path $ocrRuntimeDir "tessdata\configs\tsv")
)
foreach ($requiredOcrFile in $requiredOcrFiles) {
    if (-not (Test-Path $requiredOcrFile)) {
        throw "Falta un componente del runtime OCR local: $requiredOcrFile"
    }
}

New-Item -ItemType Directory -Force -Path $ocrOutputDir | Out-Null
Copy-Item (Join-Path $ocrRuntimeDir "*") $ocrOutputDir -Recurse -Force

Write-Host "Compilación completada en $outputDir"
Write-Host "Aplicación principal: $outputExe"
Write-Host "Copia compatible con el menú antiguo: $legacyOutputExe"
Write-Host "OCR local empaquetado: $ocrOutputDir"
