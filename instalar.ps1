<#
.SYNOPSIS
    Instalador unico de Word2PDF y PDF Ligero.

.DESCRIPTION
    Registra en el menu contextual del usuario actual las dos herramientas de
    esta carpeta:

    - Word2PDF: "Convertir a PDF" para .doc, .docx y .rtf.
    - PDF Ligero: "Abrir con PDF Ligero", "Combinar con PDF Ligero" y
      "Firmar PDFs" para .pdf, mas la entrada de "Abrir con".

    Instala lo que encuentra. Si falta una de las dos, lo dice y sigue con la
    otra en lugar de abortar. No necesita permisos de administrador: escribe
    solo en HKCU.

.PARAMETER SoloWord2PDF
    Instala unicamente Word2PDF.

.PARAMETER SoloPdfLigero
    Instala unicamente PDF Ligero.

.PARAMETER RegistryRoot
    Raiz del registro bajo la que se escribe. Solo se cambia en QA, para poder
    comprobar el instalador sin tocar la integracion real del usuario.
#>
param(
    [switch]$SoloWord2PDF,
    [switch]$SoloPdfLigero,
    [string]$RegistryRoot = "HKCU:"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$instalarWord2PDF = -not $SoloPdfLigero
$instalarPdfLigero = -not $SoloWord2PDF

$hechos = New-Object System.Collections.Generic.List[string]
$avisos = New-Object System.Collections.Generic.List[string]

function Write-Titulo {
    param([string]$Texto)

    Write-Host ""
    Write-Host "=========================================="
    Write-Host $Texto
    Write-Host "=========================================="
}

function Register-Word2PdfExtension {
    param(
        [string]$Extension,
        [string]$ExePath
    )

    $shellKey = Join-Path `
        $RegistryRoot `
        ("Software\Classes\SystemFileAssociations\" + $Extension + "\shell\Word2PDF")
    $commandKey = Join-Path $shellKey "command"

    New-Item -Path $shellKey -Force | Out-Null
    Set-Item -Path $shellKey -Value "Convertir a PDF"
    Set-ItemProperty -Path $shellKey -Name "Icon" -Value $ExePath
    # Player agrupa la seleccion multiple en una sola invocacion por archivo,
    # que es lo que Word2PDF espera para juntarlas en un unico lote.
    Set-ItemProperty -Path $shellKey -Name "MultiSelectModel" -Value "Player"

    New-Item -Path $commandKey -Force | Out-Null
    Set-Item -Path $commandKey -Value ('"{0}" "%1"' -f $ExePath)
}

Write-Titulo "INSTALADOR DE WORD2PDF Y PDF LIGERO"

# --------------------------------------------------------------------------
# Word2PDF
# --------------------------------------------------------------------------
if ($instalarWord2PDF) {
    $word2Pdf = Join-Path $root "Word2PDF.exe"
    if (-not (Test-Path -LiteralPath $word2Pdf)) {
        $avisos.Add(
            "Word2PDF: no se encontro Word2PDF.exe en esta carpeta, asi que no " +
            "se ha registrado.")
    }
    else {
        Write-Host "[1/2] Registrando Word2PDF para .doc, .docx y .rtf..."
        foreach ($extension in @(".doc", ".docx", ".rtf")) {
            Register-Word2PdfExtension -Extension $extension -ExePath $word2Pdf
        }

        $hechos.Add("Word2PDF: 'Convertir a PDF' en .doc, .docx y .rtf.")

        $tieneWord = $false
        try {
            $tieneWord = $null -ne (Get-ItemProperty `
                -Path "Registry::HKEY_CLASSES_ROOT\Word.Application" `
                -ErrorAction Stop)
        }
        catch {
            $tieneWord = $false
        }

        if (-not $tieneWord) {
            $avisos.Add(
                "Word2PDF necesita Microsoft Word instalado en el equipo y no " +
                "se ha detectado. El menu queda registrado, pero la conversion " +
                "fallara hasta que Word este disponible.")
        }
    }
}

# --------------------------------------------------------------------------
# PDF Ligero
# --------------------------------------------------------------------------
if ($instalarPdfLigero) {
    $carpetaFirma = Get-ChildItem -LiteralPath $root -Directory -Filter "firma*" |
        Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName "install-context-menu.ps1")
        } |
        Select-Object -First 1

    if ($null -eq $carpetaFirma) {
        $avisos.Add(
            "PDF Ligero: no se encontro su carpeta en esta ubicacion, asi que " +
            "no se ha registrado.")
    }
    else {
        $exe = Join-Path $carpetaFirma.FullName "build\output\PDFLigero.exe"
        $legacy = Join-Path $carpetaFirma.FullName "build\output\FirmaAutomatica.exe"
        if (-not (Test-Path -LiteralPath $exe) -and
            -not (Test-Path -LiteralPath $legacy)) {
            $avisos.Add(
                "PDF Ligero: falta build\output\PDFLigero.exe. Ejecuta antes " +
                "build.ps1 dentro de '" + $carpetaFirma.Name + "' y vuelve a " +
                "instalar.")
        }
        else {
            Write-Host "[2/2] Registrando PDF Ligero para .pdf..."
            & (Join-Path $carpetaFirma.FullName "install-context-menu.ps1") `
                -RegistryRoot $RegistryRoot
            if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
                throw "No se pudo registrar la integracion de PDF Ligero."
            }

            $hechos.Add(
                "PDF Ligero: 'Abrir con PDF Ligero', 'Combinar con PDF Ligero' " +
                "y 'Firmar PDFs' en .pdf.")
        }
    }
}

# --------------------------------------------------------------------------
# Resumen
# --------------------------------------------------------------------------
Write-Titulo "RESUMEN"

if ($hechos.Count -eq 0) {
    Write-Host "No se ha instalado nada."
}
else {
    Write-Host "Instalado:"
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
Write-Host "No muevas ni renombres esta carpeta despues de instalar."
Write-Host "Para abrir los PDF con doble clic, elige una vez:"
Write-Host "  Abrir con -> PDF Ligero -> Siempre"
Write-Host ""
Write-Host "Para quitar la integracion, ejecuta desinstalar.bat."

if ($hechos.Count -eq 0) {
    exit 1
}

exit 0
