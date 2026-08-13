# Prueba de extremo a extremo: detecta la tipografia de una zona, escribe un
# reemplazo reutilizandola y comprueba que el texto nuevo sale con esa misma
# fuente, no con la generica de antes.
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

$tService = $exe.GetType('FirmaAutomatica.PdfTextEditService')
$tRegion = $exe.GetType('FirmaAutomatica.PdfTextEditRegion')
$tReplacement = $exe.GetType('FirmaAutomatica.PdfTextReplacement')
$tIdentity = $exe.GetType('FirmaAutomatica.PdfEditViewIdentity')
$tProbe = $exe.GetType('FirmaAutomatica.PdfTextStyleProbe')
$tFamily = $exe.GetType('FirmaAutomatica.PdfTextEditFontFamily')
$tCatalog = $exe.GetType('FirmaAutomatica.PdfSystemFontCatalog')

$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$prepare = $tService.GetMethods($flags) |
    Where-Object { $_.Name -eq 'PrepareSelection' -and $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1
$save = $tService.GetMethods($flags) |
    Where-Object { $_.Name -eq 'Save' -and $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1
$detect = $tProbe.GetMethod('Detect', $flags)
$guess = $tCatalog.GetMethod('GuessFamily', $flags)

# Una banda ancha en el centro de la pagina, donde hay texto seguro
$info = New-Object System.IO.FileInfo($Pdf)
$identity = [Activator]::CreateInstance(
    $tIdentity,
    @([string]$Pdf, [long]$info.Length, [long]$info.LastWriteTimeUtc.Ticks))

$esperado = @('Calibri', 'Calibri', 'Times New Roman', 'Arial', 'Consolas')
$fallos = 0
$temporales = @()

Write-Host ("{0,-5} {1,-16} {2,-26} {3}" -f 'Pag', 'original', 'usada al escribir', 'ok')
Write-Host ('-' * 74)

for ($p = 1; $p -le $esperado.Count; $p++) {
    $reader = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
    try {
        $ancho = $reader.GetPageSize($p).Width
        $alto = $reader.GetPageSize($p).Height
    }
    finally { $reader.Close() }

    # La pagina entera: aqui interesa comprobar la reutilizacion de la fuente,
    # no acertar con un recorte.
    $bounds = New-Object System.Drawing.RectangleF(
        [float]0, [float]0, [float]$ancho, [float]$alto)

    $prep = $prepare.Invoke($null, [object[]]@(
        [string]$Pdf, [int]($p - 1), $bounds, $identity))

    $estilo = $prep.GetType().GetProperty('DetectedStyle').GetValue($prep, $null)
    if ($null -eq $estilo) {
        Write-Host ("{0,-5} {1,-16} {2,-26} NO" -f $p, $esperado[$p-1], '(no detectada)')
        $fallos++
        continue
    }

    $fuente = $estilo.GetType().GetProperty('FontName').GetValue($estilo, $null)
    $negrita = $estilo.GetType().GetProperty('Bold').GetValue($estilo, $null)
    $cursiva = $estilo.GetType().GetProperty('Italic').GetValue($estilo, $null)
    $region = $prep.GetType().GetProperty('Region').GetValue($prep, $null)
    $analisis = $prep.GetType().GetProperty('Analysis').GetValue($prep, $null)

    $reemplazo = [Activator]::CreateInstance(
        $tReplacement, @($region, [string]'Texto sustituido de prueba.'))
    $reemplazo.GetType().GetProperty('PreferredFontName').SetValue(
        $reemplazo, [string]$fuente, $null)
    $reemplazo.GetType().GetProperty('Bold').SetValue($reemplazo, [bool]$negrita, $null)
    $reemplazo.GetType().GetProperty('Italic').SetValue($reemplazo, [bool]$cursiva, $null)
    $reemplazo.GetType().GetProperty('FontFamily').SetValue(
        $reemplazo, $guess.Invoke($null, [object[]]@([string]$fuente)), $null)

    $destino = Join-Path $env:TEMP ("reemplazo-p$p-" + [Guid]::NewGuid().ToString('N') + '.pdf')
    $temporales += $destino
    $resultado = $save.Invoke($null, [object[]]@(
        [string]$Pdf, [string]$destino, $analisis, $reemplazo))

    $usada = $resultado.GetType().GetProperty('FontDisplayName').GetValue($resultado, $null)
    $ok = ($usada -replace '\s', '') -ieq ($esperado[$p-1] -replace '\s', '')
    if (-not $ok) { $fallos++ }

    Write-Host ("{0,-5} {1,-16} {2,-26} {3}" -f $p, $esperado[$p-1], $usada,
        $(if ($ok) { 'SI' } else { 'NO' }))
}

foreach ($t in $temporales) {
    if (Test-Path -LiteralPath $t) { Remove-Item -LiteralPath $t -Force }
}

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'El texto nuevo se escribe siempre con la fuente del original.'
    exit 0
}

Write-Host "Paginas que no reutilizaron la fuente: $fallos"
exit 1
