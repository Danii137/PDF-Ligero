# Prueba de viabilidad de la edicion directa.
#
# Tres preguntas, en orden:
#   1. Se puede saber que glifos trae la fuente incrustada?
#   2. Se puede volver a codificar texto nuevo con esa misma fuente?
#   3. Los bytes que produce coinciden con los que ya hay en el flujo?
#
# Si la 2 o la 3 fallan, la edicion directa no es viable con esta libreria.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output'),
    [string]$Pdf = (Join-Path $PSScriptRoot '..\validation-text-style-probe\muestras\muestras.pdf')
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')

if (-not (Test-Path -LiteralPath $Pdf)) {
    throw "Falta el PDF de muestra. Ejecuta antes validation-text-style-probe\crear-muestras.ps1"
}

function N { param([string]$n) New-Object iTextSharp.text.pdf.PdfName($n) }

$reader = New-Object iTextSharp.text.pdf.PdfReader((Resolve-Path -LiteralPath $Pdf).Path)
try {
    $recursos = $reader.GetPageN(1).GetAsDict((N 'Resources'))
    $fuentes = $recursos.GetAsDict((N 'Font'))
    Write-Host "Fuentes declaradas en la pagina 1: $($fuentes.Keys.Count)"
    Write-Host ''

    foreach ($clave in $fuentes.Keys) {
        # DocumentFont solo tiene constructores internos; se llega por reflexion.
        $ref = $fuentes.Get($clave)
        $tipoDf = [iTextSharp.text.pdf.DocumentFont]
        $bf = [Reflection.BindingFlags]'Instance,NonPublic,Public'
        if ($ref -is [iTextSharp.text.pdf.PRIndirectReference]) {
            $ctor = $tipoDf.GetConstructors($bf) | Where-Object {
                $_.GetParameters().Count -eq 1 -and
                $_.GetParameters()[0].ParameterType.Name -eq 'PRIndirectReference'
            } | Select-Object -First 1
            $df = $ctor.Invoke(@($ref))
        } else {
            $ctor = $tipoDf.GetConstructors($bf) | Where-Object {
                $_.GetParameters().Count -eq 1 -and
                $_.GetParameters()[0].ParameterType.Name -eq 'PdfDictionary'
            } | Select-Object -First 1
            $df = $ctor.Invoke(@($fuentes.GetAsDict($clave)))
        }

        Write-Host ("--- {0}  ->  {1}" -f $clave, $df.PostscriptFontName)

        # 1. Cobertura de glifos
        # Se escriben con escapes para no depender de la codificacion del archivo
        $muestra = "Texto 0123456789 $([char]0xF1)$([char]0xD1)$([char]0xE1)$([char]0xE9)$([char]0xED)$([char]0xF3)$([char]0xFA) $([char]0x20AC)"
        $faltan = @()
        foreach ($c in $muestra.ToCharArray()) {
            if (-not $df.CharExists([int]$c)) { $faltan += $c }
        }
        if ($faltan.Count -eq 0) {
            Write-Host '    cobertura: tiene todos los caracteres de la muestra'
        } else {
            Write-Host ("    cobertura: FALTAN {0} -> '{1}'" -f $faltan.Count, ($faltan -join ''))
        }

        # 2. Recodificacion
        try {
            $bytes = $df.ConvertToBytes('Texto')
            Write-Host ("    ConvertToBytes('Texto') = {0} bytes: {1}" -f `
                $bytes.Length, (($bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' '))
        }
        catch {
            Write-Host ("    ConvertToBytes FALLA: {0}" -f $_.Exception.Message)
        }
    }

    # 3. Comparar con lo que hay de verdad en el flujo
    Write-Host ''
    Write-Host '=== primeros operadores de texto del flujo de la pagina 1 ==='
    $contenido = [iTextSharp.text.pdf.PdfReader]::GetPageContent($reader.GetPageN(1))
    $texto = [System.Text.Encoding]::ASCII.GetString($contenido)
    $recorte = $texto.Substring(0, [Math]::Min(700, $texto.Length))
    Write-Host ($recorte -replace "`r`n|`n", ' ')
}
finally { $reader.Close() }
