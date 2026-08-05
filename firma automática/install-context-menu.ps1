param(
    # Raiz del registro bajo la que se escribe todo. Solo se cambia en QA, para
    # poder comprobar el instalador sin tocar la integracion real del usuario.
    [string]$RegistryRoot = "HKCU:"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $root "build\output\PDFLigero.exe"
$legacyExePath = Join-Path $root "build\output\FirmaAutomatica.exe"
$iconPath = Join-Path $root "build\output\PDFLigero.ico"
$iconGenerator = Join-Path $root "generate-icon.ps1"
$iconSourcePng = Join-Path $root "assets\PDFLigero.png"

if (-not (Test-Path $exePath)) {
    if (Test-Path $legacyExePath) {
        $exePath = $legacyExePath
    }
    else {
        throw "No existe $exePath. Ejecuta antes build.ps1."
    }
}

if ((Test-Path $iconGenerator) -and (Test-Path $iconSourcePng)) {
    & $iconGenerator -InputPngPath $iconSourcePng -OutputIcoPath $iconPath
}

$menuIconPath = if (Test-Path $iconPath) { $iconPath } else { $exePath }

$associationsKey = Join-Path $RegistryRoot "Software\Classes\SystemFileAssociations\.pdf\shell"
$openCommandKey = Join-Path $associationsKey "PDFLigero.Open"
$openCommandSubKey = Join-Path $openCommandKey "command"
$commandKey = Join-Path $associationsKey "FirmarPDFs"
$commandSubKey = Join-Path $commandKey "command"
$mergeCommandKey = Join-Path $associationsKey "PDFLigero.Merge"
$mergeCommandSubKey = Join-Path $mergeCommandKey "command"

New-Item -Path $openCommandKey -Force | Out-Null
Set-Item -Path $openCommandKey -Value "Abrir con PDF Ligero"
Set-ItemProperty -Path $openCommandKey -Name "Icon" -Value $menuIconPath

New-Item -Path $openCommandSubKey -Force | Out-Null
Set-Item -Path $openCommandSubKey -Value ('"{0}" --open "%1"' -f $exePath)

New-Item -Path $commandKey -Force | Out-Null
Set-Item -Path $commandKey -Value "Firmar PDFs"
Set-ItemProperty -Path $commandKey -Name "MultiSelectModel" -Value "Player"
Set-ItemProperty -Path $commandKey -Name "Icon" -Value $menuIconPath

New-Item -Path $commandSubKey -Force | Out-Null
Set-Item -Path $commandSubKey -Value ('"{0}" --sign "%1"' -f $exePath)

New-Item -Path $mergeCommandKey -Force | Out-Null
Set-Item -Path $mergeCommandKey -Value "Combinar con PDF Ligero"
Set-ItemProperty -Path $mergeCommandKey -Name "MultiSelectModel" -Value "Player"
Set-ItemProperty -Path $mergeCommandKey -Name "Icon" -Value $menuIconPath

New-Item -Path $mergeCommandSubKey -Force | Out-Null
Set-Item -Path $mergeCommandSubKey -Value ('"{0}" --merge "%1"' -f $exePath)

$applicationKey = Join-Path $RegistryRoot "Software\Classes\Applications\PDFLigero.exe"
$applicationDefaultIconKey = Join-Path $applicationKey "DefaultIcon"
$applicationOpenCommandKey = Join-Path $applicationKey "shell\open\command"
$supportedTypesKey = Join-Path $applicationKey "SupportedTypes"
New-Item -Path $applicationKey -Force | Out-Null
Set-ItemProperty -Path $applicationKey -Name "FriendlyAppName" -Value "PDF Ligero"
New-Item -Path $applicationDefaultIconKey -Force | Out-Null
Set-Item -Path $applicationDefaultIconKey -Value ('"{0}",0' -f $iconPath)
New-Item -Path $applicationOpenCommandKey -Force | Out-Null
Set-Item -Path $applicationOpenCommandKey -Value ('"{0}" --open "%1"' -f $exePath)
New-Item -Path $supportedTypesKey -Force | Out-Null
New-ItemProperty -Path $supportedTypesKey -Name ".pdf" -PropertyType String -Value "" -Force | Out-Null

$explorerKey = Join-Path $RegistryRoot "Software\Microsoft\Windows\CurrentVersion\Explorer"
$installerStateKey = Join-Path $RegistryRoot "Software\PDFLigero\Installer"
if (-not (Test-Path $explorerKey)) {
    New-Item -Path $explorerKey -Force | Out-Null
}

if (-not (Test-Path $installerStateKey)) {
    New-Item -Path $installerStateKey -Force | Out-Null
    $existingPromptValue = Get-ItemProperty -Path $explorerKey -Name "MultipleInvokePromptMinimum" -ErrorAction SilentlyContinue
    if ($null -ne $existingPromptValue) {
        New-ItemProperty -Path $installerStateKey -Name "HadMultipleInvokePromptMinimum" -PropertyType DWord -Value 1 -Force | Out-Null
        New-ItemProperty -Path $installerStateKey -Name "PreviousMultipleInvokePromptMinimum" -PropertyType DWord -Value ([int]$existingPromptValue.MultipleInvokePromptMinimum) -Force | Out-Null
    }
    else {
        New-ItemProperty -Path $installerStateKey -Name "HadMultipleInvokePromptMinimum" -PropertyType DWord -Value 0 -Force | Out-Null
    }
}

New-ItemProperty -Path $explorerKey -Name "MultipleInvokePromptMinimum" -PropertyType DWord -Value 100 -Force | Out-Null

if (-not ("PDFLigeroShellNotification" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class PDFLigeroShellNotification
{
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
"@
}
[PDFLigeroShellNotification]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Integración de PDF Ligero instalada."
Write-Host "Ya puedes abrir, combinar o firmar PDFs desde el menú contextual."
Write-Host "Para usarlo con doble clic, elige 'Abrir con' -> 'PDF Ligero' -> 'Siempre'."
