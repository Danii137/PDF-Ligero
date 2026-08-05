$openCommandKey = "HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PDFLigero.Open"
$commandKey = "HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\FirmarPDFs"
$mergeCommandKey = "HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PDFLigero.Merge"
$applicationKey = "HKCU:\Software\Classes\Applications\PDFLigero.exe"

if (Test-Path $mergeCommandKey) {
    Remove-Item -LiteralPath $mergeCommandKey -Recurse -Force
}

if (Test-Path $commandKey) {
    Remove-Item -LiteralPath $commandKey -Recurse -Force
}

if (Test-Path $openCommandKey) {
    Remove-Item -LiteralPath $openCommandKey -Recurse -Force
}

if (Test-Path $applicationKey) {
    Remove-Item -LiteralPath $applicationKey -Recurse -Force
}

$explorerKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer"
$installerStateKey = "HKCU:\Software\PDFLigero\Installer"
if (Test-Path $installerStateKey) {
    $installerState = Get-ItemProperty -Path $installerStateKey
    $currentPromptValue = Get-ItemProperty -Path $explorerKey -Name "MultipleInvokePromptMinimum" -ErrorAction SilentlyContinue
    if ($null -ne $currentPromptValue -and [int]$currentPromptValue.MultipleInvokePromptMinimum -eq 100) {
        if ([int]$installerState.HadMultipleInvokePromptMinimum -eq 1) {
            New-ItemProperty `
                -Path $explorerKey `
                -Name "MultipleInvokePromptMinimum" `
                -PropertyType DWord `
                -Value ([int]$installerState.PreviousMultipleInvokePromptMinimum) `
                -Force | Out-Null
        }
        else {
            Remove-ItemProperty -Path $explorerKey -Name "MultipleInvokePromptMinimum" -ErrorAction SilentlyContinue
        }
    }

    Remove-Item -Path $installerStateKey -Recurse -Force
}

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

Write-Host "Integración de PDF Ligero eliminada."
