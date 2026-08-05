$ErrorActionPreference = "Stop"

$keyPath = "HKLM:\Software\Classes\SystemFileAssociations\.pdf\shell\FirmarPDF"
$backupPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "backup-context-menu-firmar-pdf-digitalmente.reg"

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    throw "Este script debe ejecutarse como administrador."
}

if (Test-Path $keyPath) {
    reg export "HKLM\Software\Classes\SystemFileAssociations\.pdf\shell\FirmarPDF" $backupPath /y | Out-Null
    Remove-Item -Path $keyPath -Recurse -Force
    Write-Host "Entrada antigua eliminada correctamente."
    Write-Host "Copia de seguridad guardada en: $backupPath"
}
else {
    Write-Host "La entrada antigua ya no existe."
}
