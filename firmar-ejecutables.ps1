<#
.SYNOPSIS
    Firma con Authenticode los ejecutables de Word2PDF y PDF Ligero, o comprueba
    su firma actual.

.DESCRIPTION
    Sin firma Authenticode, Windows SmartScreen avisa al usuario de que el
    programa es de "editor desconocido" cada vez que lo ejecuta desde una
    descarga o una carpeta de red.

    Este script no puede firmar por si solo: hace falta un certificado de firma
    de codigo (EKU 1.3.6.1.5.5.7.3.3). Un certificado de firma de documentos,
    como el que usa PDF Ligero para firmar PDFs, NO sirve.

    Usa Set-AuthenticodeSignature, que viene con Windows PowerShell, de modo que
    no exige instalar el SDK de Windows solo para firmar.

    La marca de tiempo no es opcional en la practica: sin ella la firma deja de
    validar en cuanto caduca el certificado, aunque el binario no haya cambiado.

.PARAMETER Thumbprint
    Huella del certificado en Cert:\CurrentUser\My.

.PARAMETER CertificatePath
    Ruta a un .pfx/.p12. Se pedira la contrasena de forma interactiva; no se
    guarda en ningun sitio.

.PARAMETER TimestampUrl
    Servidor RFC 3161 de sellado de tiempo.

.PARAMETER SoloVerificar
    No firma: solo informa del estado de firma actual de cada archivo.

.EXAMPLE
    .\firmar-ejecutables.ps1 -SoloVerificar

.EXAMPLE
    .\firmar-ejecutables.ps1 -Thumbprint A1B2C3...

.EXAMPLE
    .\firmar-ejecutables.ps1 -CertificatePath C:\certs\agoin-codesign.pfx
#>
param(
    [string]$Thumbprint,
    [string]$CertificatePath,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SoloVerificar
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-TargetFiles {
    $targets = New-Object System.Collections.Generic.List[string]

    $word2Pdf = Join-Path $root "Word2PDF.exe"
    if (Test-Path -LiteralPath $word2Pdf) {
        $targets.Add($word2Pdf)
    }

    $carpetaFirma = Get-ChildItem -LiteralPath $root -Directory -Filter "firma*" |
        Select-Object -First 1
    if ($null -ne $carpetaFirma) {
        foreach ($nombre in @("PDFLigero.exe", "FirmaAutomatica.exe")) {
            $ruta = Join-Path $carpetaFirma.FullName "build\output\$nombre"
            if (Test-Path -LiteralPath $ruta) {
                $targets.Add($ruta)
            }
        }
    }

    return $targets
}

function Show-SignatureState {
    param([string]$Path)

    $firma = Get-AuthenticodeSignature -LiteralPath $Path
    $nombre = $Path.Substring($root.Length).TrimStart("\")

    switch ($firma.Status) {
        "Valid" {
            $emisor = "(desconocido)"
            if ($null -ne $firma.SignerCertificate) {
                $emisor = ($firma.SignerCertificate.Subject -split ",")[0]
            }

            $sello = "sin marca de tiempo"
            if ($null -ne $firma.TimeStamperCertificate) {
                $sello = "con marca de tiempo"
            }

            Write-Host ("  FIRMADO   {0}" -f $nombre)
            Write-Host ("            {0}, {1}" -f $emisor, $sello)
        }
        "NotSigned" {
            Write-Host ("  SIN FIRMA {0}" -f $nombre)
        }
        default {
            Write-Host ("  PROBLEMA  {0}" -f $nombre)
            Write-Host ("            {0}: {1}" -f $firma.Status, $firma.StatusMessage)
        }
    }

    return $firma.Status
}

function Resolve-SigningCertificate {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        if (-not (Test-Path -LiteralPath $CertificatePath)) {
            throw "No se encuentra el certificado: $CertificatePath"
        }

        $password = Read-Host -AsSecureString `
            "Contrasena del certificado (no se guarda)"
        return New-Object `
            System.Security.Cryptography.X509Certificates.X509Certificate2 `
            -ArgumentList $CertificatePath, $password
    }

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $limpio = $Thumbprint -replace "\s", ""
        $certificado = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Thumbprint -eq $limpio } |
            Select-Object -First 1
        if ($null -eq $certificado) {
            throw "No hay ningun certificado con la huella $limpio en Cert:\CurrentUser\My."
        }

        return $certificado
    }

    # Sin parametros: buscar uno valido para firmar codigo y explicarlo si no hay.
    $candidatos = @(Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            @($_.EnhancedKeyUsageList |
                Where-Object { $_.ObjectId -eq "1.3.6.1.5.5.7.3.3" }).Count -gt 0
        })

    if ($candidatos.Count -eq 0) {
        throw @"
No hay ningun certificado de firma de codigo en Cert:\CurrentUser\My.

Hace falta uno con el uso mejorado de clave 1.3.6.1.5.5.7.3.3 (Code Signing).
Un certificado de firma de documentos, como el que usa PDF Ligero para firmar
PDFs, no sirve para esto: son usos distintos.

Los certificados OV y EV de firma de codigo exigen desde 2023 que la clave
privada viva en un token fisico o en un HSM en la nube, asi que el proveedor
entrega un dispositivo o unas credenciales, no un .pfx.

Indica el certificado con -Thumbprint o -CertificatePath cuando lo tengas.
"@
    }

    if ($candidatos.Count -gt 1) {
        Write-Host "Hay varios certificados de firma de codigo. Elige uno con -Thumbprint:"
        foreach ($c in $candidatos) {
            Write-Host ("  {0}  {1}  caduca {2:dd/MM/yyyy}" -f `
                $c.Thumbprint, ($c.Subject -split ",")[0], $c.NotAfter)
        }

        throw "Indica el certificado con -Thumbprint."
    }

    return $candidatos[0]
}

$objetivos = Get-TargetFiles
if ($objetivos.Count -eq 0) {
    throw "No se encontro ningun ejecutable que firmar. Ejecuta antes build.ps1."
}

Write-Host ""
Write-Host "=========================================="
Write-Host "FIRMA AUTHENTICODE"
Write-Host "=========================================="
Write-Host ""
Write-Host "Estado actual:"
$estados = @()
foreach ($objetivo in $objetivos) {
    $estados += Show-SignatureState -Path $objetivo
}

if ($SoloVerificar) {
    Write-Host ""
    $sinFirmar = @($estados | Where-Object { $_ -eq "NotSigned" }).Count
    if ($sinFirmar -eq 0) {
        Write-Host "RESULTADO=FIRMADO"
        exit 0
    }

    Write-Host ("RESULTADO=SIN_FIRMAR ({0} de {1} archivos)" -f `
        $sinFirmar, $objetivos.Count)
    exit 1
}

$certificado = Resolve-SigningCertificate
Write-Host ""
Write-Host ("Certificado: {0}" -f ($certificado.Subject -split ",")[0])
Write-Host ("Huella     : {0}" -f $certificado.Thumbprint)
Write-Host ("Caduca     : {0:dd/MM/yyyy}" -f $certificado.NotAfter)
Write-Host ("Sellado    : {0}" -f $TimestampUrl)
Write-Host ""

$fallos = 0
foreach ($objetivo in $objetivos) {
    $nombre = $objetivo.Substring($root.Length).TrimStart("\")
    $resultado = Set-AuthenticodeSignature `
        -LiteralPath $objetivo `
        -Certificate $certificado `
        -TimestampServer $TimestampUrl `
        -HashAlgorithm SHA256

    if ($resultado.Status -eq "Valid") {
        Write-Host ("  OK    {0}" -f $nombre)
    }
    elseif ($null -ne $resultado.SignerCertificate) {
        # La firma se incrusto, pero la cadena no valida en este equipo. Pasa
        # siempre con certificados autofirmados y tambien si falta la CA
        # intermedia. El binario queda firmado, pero SmartScreen seguira
        # avisando, asi que cuenta como fallo.
        Write-Host ("  AVISO {0}: firmado, pero la cadena no valida aqui." -f $nombre)
        Write-Host ("            {0}" -f $resultado.StatusMessage)
        Write-Host "            Comprueba que la CA intermedia este instalada y"
        Write-Host "            que el certificado no sea autofirmado."
        $fallos++
    }
    else {
        Write-Host ("  ERROR {0}: {1}" -f $nombre, $resultado.StatusMessage)
        $fallos++
    }
}

Write-Host ""
Write-Host "Estado final:"
foreach ($objetivo in $objetivos) {
    Show-SignatureState -Path $objetivo | Out-Null
}

Write-Host ""
if ($fallos -gt 0) {
    Write-Host "RESULTADO=FALLO"
    exit 1
}

Write-Host "RESULTADO=FIRMADO"
Write-Host ""
Write-Host "Recuerda volver a firmar despues de cada build.ps1: recompilar"
Write-Host "genera un binario nuevo y la firma anterior deja de valer."
exit 0
