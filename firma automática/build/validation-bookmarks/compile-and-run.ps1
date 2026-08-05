$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent (Split-Path -Parent $validationDir)
$packagesDir = Join-Path $sourceRoot "packages"
$outputDir = Join-Path $validationDir "output"
$runDir = Join-Path $validationDir "run"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$references = @(
    "System.dll",
    "System.Core.dll",
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll"),
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll")
)
$referenceArgs = $references |
    ForEach-Object { "/reference:$_" }
$outputExe = Join-Path $outputDir "BookmarkFixtureAudit.exe"

& $csc `
    "/target:exe" `
    "/optimize+" `
    "/main:FirmaAutomatica.BookmarkFixtureAudit" `
    "/out:$outputExe" `
    $referenceArgs `
    (Join-Path $validationDir "BookmarkFixtureAudit.cs")
if ($LASTEXITCODE -ne 0) {
    throw "La compilación del fixture de marcadores ha fallado."
}

Copy-Item `
    (Join-Path $packagesDir "iTextSharp.5.5.13.3\lib\itextsharp.dll") `
    $outputDir `
    -Force
Copy-Item `
    (Join-Path $packagesDir "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll") `
    $outputDir `
    -Force

& $outputExe $runDir
exit $LASTEXITCODE
