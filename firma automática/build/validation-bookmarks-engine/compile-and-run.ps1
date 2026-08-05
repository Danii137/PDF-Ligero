$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$packages = Join-Path $root "packages"
$output = Join-Path $validation "output"
$run = Join-Path $validation "run"
$fixtureRun = Join-Path (Split-Path -Parent $validation) "validation-bookmarks\run"
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$itext = Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"
$bouncy = Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"
$pdfium = Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$exe = Join-Path $output "BookmarkEngineQa.exe"
& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.BookmarkEngineQa `
    "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    "/reference:$itext" `
    "/reference:$bouncy" `
    "/reference:$pdfium" `
    (Join-Path $root "PdfProblemDiagnostics.cs") `
    (Join-Path $root "PdfBookmarkService.cs") `
    (Join-Path $validation "BookmarkEngineQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar BookmarkEngineQa."
}

Copy-Item $itext $output -Force
Copy-Item $bouncy $output -Force
& $exe `
    $run `
    (Join-Path $fixtureRun "bookmark-advanced-fixture.pdf") `
    (Join-Path $fixtureRun "bookmark-advanced-signed-fixture.pdf")
if ($LASTEXITCODE -ne 0) {
    throw "BookmarkEngineQa ha fallado."
}

$pdfiumViewer = Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$pdfiumNative = Join-Path $packages "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll"
$renderExe = Join-Path $output "BookmarkRenderQa.exe"
& $csc `
    /nologo `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.BookmarkRenderQa `
    "/out:$renderExe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    "/reference:$pdfiumViewer" `
    (Join-Path $validation "BookmarkRenderQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar BookmarkRenderQa."
}

Copy-Item $pdfiumViewer $output -Force
Copy-Item $pdfiumNative $output -Force
& $renderExe `
    (Join-Path $fixtureRun "bookmark-advanced-fixture.pdf") `
    (Join-Path $run "bookmark-engine-result.pdf") `
    $run
if ($LASTEXITCODE -ne 0) {
    throw "La comparación visual de marcadores ha fallado."
}
