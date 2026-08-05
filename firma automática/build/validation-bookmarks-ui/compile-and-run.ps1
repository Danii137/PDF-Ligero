$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$packages = Join-Path $root "packages"
$output = Join-Path $validationDir "output"
$run = Join-Path $validationDir "run"
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll")
)
$sources = @(
    (Join-Path $root "AppBranding.cs"),
    (Join-Path $root "PdfBookmarkService.cs"),
    (Join-Path $root "PdfBookmarkEditorForm.cs"),
    (Join-Path $validationDir "BookmarkUiQa.cs")
)
$referenceArgs = $references | ForEach-Object { "/reference:$_" }
$exe = Join-Path $output "BookmarkUiQa.exe"

& $csc `
    /target:exe `
    /optimize `
    /main:FirmaAutomatica.BookmarkUiQa `
    "/out:$exe" `
    $referenceArgs `
    $sources
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar la prueba de interfaz de marcadores."
}

Copy-Item `
    (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $output -Force
Copy-Item `
    (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $output -Force

& $exe $run
if ($LASTEXITCODE -ne 0) {
    throw "La prueba de interfaz de marcadores ha fallado."
}
