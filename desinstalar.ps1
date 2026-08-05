<#
.SYNOPSIS
    Desinstalador unico de Word2PDF y PDF Ligero.

.DESCRIPTION
    Quita del menu contextual del usuario actual las entradas de las dos
    herramientas. No borra archivos: solo deshace el registro que creo
    instalar.ps1.

.PARAMETER SoloWord2PDF
    Quita unicamente Word2PDF.

.PARAMETER SoloPdfLigero
    Quita unicamente PDF Ligero.

.PARAMETER RegistryRoot
    Raiz del registro. Solo se cambia en QA.
#>
param(
    [switch]$SoloWord2PDF,
    [switch]$SoloPdfLigero,
    [string]$RegistryRoot = "HKCU:"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$quitarWord2PDF = -not $SoloPdfLigero
$quitarPdfLigero = -not $SoloWord2PDF

$hechos = New-Object System.Collections.Generic.List[string]
$avisos = New-Object System.Collections.Generic.List[string]

Write-Host ""
Write-Host "=========================================="
Write-Host "DESINSTALADOR DE WORD2PDF Y PDF LIGERO"
Write-Host "=========================================="

if ($quitarWord2PDF) {
    $quitadas = 0
    foreach ($extension in @(".doc", ".docx", ".rtf")) {
        $shellKey = Join-Path `
            $RegistryRoot `
            ("Software\Classes\SystemFileAssociations\" + $extension +
             "\shell\Word2PDF")
        if (Test-Path -LiteralPath $shellKey) {
            Remove-Item -LiteralPath $shellKey -Recurse -Force
            $quitadas++
        }
    }

    if ($quitadas -gt 0) {
        $hechos.Add(
            "Word2PDF: retirado de " + $quitadas + " extension(es).")
    }
    else {
        $avisos.Add("Word2PDF no estaba registrado.")
    }
}

if ($quitarPdfLigero) {
    $carpetaFirma = Get-ChildItem -LiteralPath $root -Directory -Filter "firma*" |
        Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName "unregister-context-menu.ps1")
        } |
        Select-Object -First 1

    if ($null -eq $carpetaFirma) {
        $avisos.Add(
            "PDF Ligero: no se encontro su carpeta, asi que no se ha podido " +
            "retirar automaticamente.")
    }
    else {
        & (Join-Path $carpetaFirma.FullName "unregister-context-menu.ps1") `
            -RegistryRoot $RegistryRoot
        $hechos.Add("PDF Ligero: retiradas sus entradas de .pdf y 'Abrir con'.")
    }
}

Write-Host ""
if ($hechos.Count -eq 0) {
    Write-Host "No habia nada que quitar."
}
else {
    Write-Host "Retirado:"
    foreach ($linea in $hechos) {
        Write-Host ("  - " + $linea)
    }
}

if ($avisos.Count -gt 0) {
    Write-Host ""
    Write-Host "Avisos:"
    foreach ($linea in $avisos) {
        Write-Host ("  ! " + $linea)
    }
}

Write-Host ""
Write-Host "Los archivos de la carpeta no se han tocado."
exit 0
