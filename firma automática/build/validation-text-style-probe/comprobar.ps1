# Ejecuta PdfTextStyleProbe.Detect sobre cada pagina del PDF de muestra y
# compara lo detectado con lo que se pidio a Word.
#
# Llama al codigo real del ejecutable por reflexion, porque las clases son
# internal: asi se prueba lo que se distribuye, no una copia.
param(
    [string]$Pdf = (Join-Path $PSScriptRoot 'muestras\muestras.pdf'),
    [string]$Salida = (Join-Path $PSScriptRoot '..\output')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Pdf)) {
    throw "No existe el PDF de muestra: $Pdf. Ejecuta antes crear-muestras.ps1"
}

$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

$tipoProbe = $exe.GetType('FirmaAutomatica.PdfTextStyleProbe')
$tipoRegion = $exe.GetType('FirmaAutomatica.PdfTextEditRegion')
$detect = $tipoProbe.GetMethod(
    'Detect',
    [Reflection.BindingFlags]'Static,Public,NonPublic')

$esperado = @(
    @{ Fuente = 'Calibri';       Tamano = 11.0; Negrita = $false; Cursiva = $false },
    @{ Fuente = 'Calibri';       Tamano = 16.0; Negrita = $true;  Cursiva = $false },
    @{ Fuente = 'TimesNewRoman'; Tamano = 12.0; Negrita = $false; Cursiva = $true  },
    @{ Fuente = 'Arial';         Tamano = 9.5;  Negrita = $false; Cursiva = $false },
    @{ Fuente = 'Consolas';      Tamano = 10.0; Negrita = $false; Cursiva = $false }
)

$reader = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
$fallos = 0

try {
    Write-Host ("{0,-5} {1,-34} {2,-34} {3}" -f 'Pag', 'esperado', 'detectado', 'ok')
    Write-Host ('-' * 92)

    for ($p = 1; $p -le $reader.NumberOfPages; $p++) {
        # Toda la pagina
        $region = [Activator]::CreateInstance(
            $tipoRegion, @([int]$p, [double]0, [double]0, [double]1, [double]1))
        $estilo = $detect.Invoke($null, [object[]]@($reader.PSObject.BaseObject, $region))

        if ($p -gt $esperado.Count) { break }
        $e = $esperado[$p - 1]

        if ($null -eq $estilo) {
            Write-Host ("{0,-5} {1,-34} {2,-34} NO" -f $p, $e.Fuente, '(nada detectado)')
            $fallos++
            continue
        }

        $fuente = $estilo.GetType().GetProperty('FontName').GetValue($estilo, $null)
        $tamano = $estilo.GetType().GetProperty('FontSizePoints').GetValue($estilo, $null)
        $negrita = $estilo.GetType().GetProperty('Bold').GetValue($estilo, $null)
        $cursiva = $estilo.GetType().GetProperty('Italic').GetValue($estilo, $null)
        $subset = $estilo.GetType().GetProperty('Subset').GetValue($estilo, $null)

        $fuenteOk = $fuente -replace '[\s,-]', '' -ieq ($e.Fuente -replace '[\s,-]', '')
        $tamanoOk = [Math]::Abs([double]$tamano - $e.Tamano) -le 0.35
        $estiloOk = ($negrita -eq $e.Negrita) -and ($cursiva -eq $e.Cursiva)
        $ok = $fuenteOk -and $tamanoOk -and $estiloOk
        if (-not $ok) { $fallos++ }

        $textoEsperado = "{0} {1} pt{2}{3}" -f $e.Fuente, $e.Tamano,
            $(if ($e.Negrita) { ' negrita' } else { '' }),
            $(if ($e.Cursiva) { ' cursiva' } else { '' })
        $textoDetectado = "{0} {1:N2} pt{2}{3}{4}" -f $fuente, $tamano,
            $(if ($negrita) { ' negrita' } else { '' }),
            $(if ($cursiva) { ' cursiva' } else { '' }),
            $(if ($subset) { ' [subconjunto]' } else { '' })

        Write-Host ("{0,-5} {1,-34} {2,-34} {3}" -f $p, $textoEsperado, $textoDetectado,
            $(if ($ok) { 'SI' } else { 'NO' }))
    }
}
finally {
    $reader.Close()
}

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'Todas las paginas detectadas correctamente.'
    exit 0
}

Write-Host "Paginas con diferencias: $fallos"
exit 1
