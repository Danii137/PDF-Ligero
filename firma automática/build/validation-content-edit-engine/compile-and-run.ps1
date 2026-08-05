$ErrorActionPreference = "Stop"

$validationRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent (Split-Path -Parent $validationRoot)
$packages = Join-Path $sourceRoot "packages"
$output = Join-Path $validationRoot "output"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Force -Path $output | Out-Null

$itext = Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"
$bouncy = Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll"
$pdfiumViewer = Join-Path $packages "PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"
$pdfiumNative = Join-Path $packages "PdfiumViewer.Native.x86_64.v8-xfa.2018.4.8.256\Build\x64\pdfium.dll"
$exe = Join-Path $output "ContentEditEngineQa.exe"

& $csc /nologo /target:exe /optimize "/out:$exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Xml.dll `
    "/reference:$itext" `
    "/reference:$bouncy" `
    "/reference:$pdfiumViewer" `
    (Join-Path $sourceRoot "PdfTextEditService.cs") `
    (Join-Path $sourceRoot "PdfEditSession.cs") `
    (Join-Path $validationRoot "ContentEditEngineQa.cs")
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar ContentEditEngineQa."
}

Copy-Item $itext $output -Force
Copy-Item $bouncy $output -Force
Copy-Item $pdfiumViewer $output -Force
Copy-Item $pdfiumNative $output -Force

& $exe
if ($LASTEXITCODE -ne 0) {
    throw "ContentEditEngineQa devolvio un error."
}
