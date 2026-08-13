# QA de la edicion directa de texto.
#
# Comprueba lo que de verdad distingue esta funcion de cubrir y escribir encima:
# que el texto antiguo DESAPARECE del documento, no que quede tapado.
#
# Va por el camino real, PdfTextEditService.Save, para que se ejecute tambien la
# validacion posterior que protege el resto del documento.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output'),
    [string]$Pdf = (Join-Path $PSScriptRoot '..\validation-text-style-probe\muestras\muestras.pdf')
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

if (-not (Test-Path -LiteralPath $Pdf)) {
    throw "Falta el PDF de muestra. Ejecuta antes validation-text-style-probe\crear-muestras.ps1"
}
$Pdf = (Resolve-Path -LiteralPath $Pdf).Path

$tDirect = $exe.GetType('FirmaAutomatica.PdfDirectTextEditService')
$tEdit = $exe.GetType('FirmaAutomatica.PdfTextEditService')
$tRegion = $exe.GetType('FirmaAutomatica.PdfTextEditRegion')
$tReplacement = $exe.GetType('FirmaAutomatica.PdfTextReplacement')
$tIdentity = $exe.GetType('FirmaAutomatica.PdfEditViewIdentity')
$tCatalog = $exe.GetType('FirmaAutomatica.PdfSystemFontCatalog')
$bf = [Reflection.BindingFlags]'Static,Public,NonPublic'

$analyze = $tDirect.GetMethod('Analyze', $bf)
$guessFamily = $tCatalog.GetMethod('GuessFamily', $bf)
$prepare = $tEdit.GetMethods($bf) |
    Where-Object { $_.Name -eq 'PrepareSelection' -and $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1
$save = $tEdit.GetMethods($bf) |
    Where-Object { $_.Name -eq 'Save' -and $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1

$trabajo = Join-Path $PSScriptRoot 'trabajo'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

$fallos = 0
function Comprobar {
    param([string]$titulo, [bool]$ok, [string]$detalle)
    if ($ok) { Write-Host ("  OK    {0}" -f $titulo) }
    else { Write-Host ("  FALLA {0}  ->  {1}" -f $titulo, $detalle); $script:fallos++ }
}

function Extraer {
    param([string]$ruta, [int]$pagina)
    $r = New-Object iTextSharp.text.pdf.PdfReader($ruta)
    try {
        return [iTextSharp.text.pdf.parser.PdfTextExtractor]::GetTextFromPage($r, $pagina)
    }
    finally { $r.Close() }
}

function Region {
    param([int]$pagina)
    [Activator]::CreateInstance(
        $tRegion, @([int]$pagina, [double]0, [double]0, [double]1, [double]1))
}

# ---------------------------------------------------------------- analisis
Write-Host '=== que se puede hacer con cada texto propuesto ==='
$reader = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
try {
    $casos = @(
        @{ Texto = 'Texto sustituido en Calibri'; Espera = 'InPlace' },
        @{ Texto = 'Presupuesto de 47.900 euros'; Espera = 'RewriteWithSystemFont' }
    )
    foreach ($caso in $casos) {
        $cap = $analyze.Invoke($null, [object[]]@(
            $reader.PSObject.BaseObject, (Region 1), [string]$caso.Texto))
        $modo = $cap.Mode.ToString()
        $faltan = $cap.MissingCharacters
        Write-Host ("  '{0}'" -f $caso.Texto)
        Write-Host ("     modo: {0}{1}" -f $modo,
            $(if ($faltan) { "   faltan en la fuente: '$faltan'" } else { '' }))
        Comprobar ("Modo detectado para '" + $caso.Texto.Substring(0, 16) + "...'") `
            ($modo -eq $caso.Espera) "esperaba $($caso.Espera), dio $modo"
    }
}
finally { $reader.Close() }

# --------------------------------------------------- sustitucion por Save
$info = New-Object System.IO.FileInfo($Pdf)
$identity = [Activator]::CreateInstance(
    $tIdentity, @([string]$Pdf, [long]$info.Length, [long]$info.LastWriteTimeUtc.Ticks))

function Sustituir {
    param([string]$nuevo, [string]$destino)

    $r = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
    try { $ancho = $r.GetPageSize(1).Width; $alto = $r.GetPageSize(1).Height }
    finally { $r.Close() }

    $bounds = New-Object System.Drawing.RectangleF(
        [float]0, [float]0, [float]$ancho, [float]$alto)
    $prep = $prepare.Invoke($null, [object[]]@([string]$Pdf, [int]0, $bounds, $identity))
    $region = $prep.GetType().GetProperty('Region').GetValue($prep, $null)
    $analisis = $prep.GetType().GetProperty('Analysis').GetValue($prep, $null)
    $estilo = $prep.GetType().GetProperty('DetectedStyle').GetValue($prep, $null)
    $fuente = if ($estilo) {
        $estilo.GetType().GetProperty('FontName').GetValue($estilo, $null)
    } else { 'Calibri' }

    $reemplazo = [Activator]::CreateInstance($tReplacement, @($region, [string]$nuevo))
    $reemplazo.ReplaceInPlace = $true
    $reemplazo.PreferredFontName = [string]$fuente
    $reemplazo.FontFamily = $guessFamily.Invoke($null, [object[]]@([string]$fuente))

    $resultado = $save.Invoke($null, [object[]]@(
        [string]$Pdf, [string]$destino, $analisis, $reemplazo))
    return $resultado.GetType().GetProperty('FontDisplayName').GetValue($resultado, $null)
}

$fraseVieja = 'Texto de muestra en Calibri de 11 puntos.'
Comprobar 'La muestra contiene la frase original' (
    (Extraer $Pdf 1).Contains($fraseVieja)) 'la muestra no era la esperada'

Write-Host ''
Write-Host '=== sustitucion con la fuente incrustada del propio PDF ==='
$nuevoA = 'Texto sustituido en Calibri'
$destinoA = Join-Path $trabajo 'en-sitio.pdf'
$fuenteA = Sustituir $nuevoA $destinoA
Write-Host ("  fuente usada: {0}" -f $fuenteA)

$despuesA = Extraer $destinoA 1
Comprobar 'Aparece el texto nuevo' ($despuesA.Contains($nuevoA)) 'no se encuentra'
Comprobar 'El texto ANTIGUO ha desaparecido' (
    -not $despuesA.Contains($fraseVieja)) 'el texto viejo sigue extrayendose'
Comprobar 'Reutiliza la fuente incrustada del PDF' (
    "$fuenteA" -like '*Calibri*') "uso: $fuenteA"

Write-Host ''
Write-Host '=== sustitucion con caracteres que la fuente no trae ==='
$nuevoB = 'Presupuesto de 47.900 euros'
$destinoB = Join-Path $trabajo 'con-fuente-sistema.pdf'
$fuenteB = Sustituir $nuevoB $destinoB
Write-Host ("  fuente usada: {0}" -f $fuenteB)

$despuesB = Extraer $destinoB 1
Comprobar 'Aparece el texto nuevo con digitos' (
    $despuesB.Contains('47.900')) 'no se encuentra'
Comprobar 'El texto ANTIGUO tambien desaparece' (
    -not $despuesB.Contains($fraseVieja)) 'el texto viejo sigue extrayendose'

# --------------------------------------------------- el resto no se toca
Write-Host ''
Write-Host '=== el resto del documento ==='
foreach ($destino in @($destinoA, $destinoB)) {
    $nombre = [System.IO.Path]::GetFileName($destino)
    $r = New-Object iTextSharp.text.pdf.PdfReader($destino)
    try {
        Comprobar "$nombre conserva las 5 paginas" ($r.NumberOfPages -eq 5) `
            "$($r.NumberOfPages)"
    }
    finally { $r.Close() }

    $iguales = $true
    foreach ($p in 2..5) {
        if ((Extraer $Pdf $p) -ne (Extraer $destino $p)) { $iguales = $false }
    }
    Comprobar "$nombre no altera las paginas 2 a 5" $iguales 'alguna cambio'
}

Comprobar 'El PDF de origen sigue igual' (
    (New-Object System.IO.FileInfo($Pdf)).Length -eq $info.Length) 'cambio de tamano'

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'RESULTADO: PASS'
    exit 0
}
Write-Host "RESULTADO: FALLA ($fallos comprobaciones)"
exit 1
