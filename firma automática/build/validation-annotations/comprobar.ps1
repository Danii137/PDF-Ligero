# QA de la capa de anotacion.
#
# Escribe un trazo, un subrayado y una nota; comprueba que quedan como
# anotaciones PDF de verdad y que PDFium las dibuja, que es lo que determina si
# se veran al reabrir el documento en la propia aplicacion.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output')
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')
$env:PATH = $salidaResuelta + ';' + $env:PATH
Add-Type -Path (Join-Path $salidaResuelta 'PdfiumViewer.dll')
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

$tItem = $exe.GetType('FirmaAutomatica.PdfAnnotationItem')
$tBatch = $exe.GetType('FirmaAutomatica.PdfAnnotationBatch')
$tService = $exe.GetType('FirmaAutomatica.PdfAnnotationService')
$tKind = $exe.GetType('FirmaAutomatica.PdfAnnotationKind')
$tIdentity = $exe.GetType('FirmaAutomatica.PdfEditViewIdentity')
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'

$trabajo = Join-Path $PSScriptRoot 'trabajo'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

# PDF de partida de una pagina con algo de texto
$origen = Join-Path $trabajo 'origen.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($origen, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    $doc.Add((New-Object iTextSharp.text.Paragraph("Memoria de proyecto basico."))) | Out-Null
    $doc.Add((New-Object iTextSharp.text.Paragraph("Segunda linea para subrayar."))) | Out-Null
    $doc.Close()
}
finally { $fs.Dispose() }

$fallos = 0
function Comprobar {
    param([string]$titulo, [bool]$ok, [string]$detalle)
    if ($ok) { Write-Host ("  OK    {0}" -f $titulo) }
    else { Write-Host ("  FALLA {0}  ->  {1}" -f $titulo, $detalle); $script:fallos++ }
}

# --- construir las marcas ---
$lote = [Activator]::CreateInstance($tBatch)

# Trazo a mano alzada en diagonal
$trazo = [Activator]::CreateInstance($tItem, @([Enum]::Parse($tKind, 'Ink'), [int]1))
$trazo.Color = [System.Drawing.Color]::FromArgb(238, 91, 61)
$trazo.WidthPoints = [float]3
$trazo.Author = 'QA'
$trazo.BeginStroke()
foreach ($i in 0..20) {
    $trazo.AddPoint((New-Object System.Drawing.PointF([float](100 + $i * 8), [float](300 + $i * 4))))
}
$lote.Add($trazo)

# Subrayado sobre la primera linea
$subrayado = [Activator]::CreateInstance($tItem, @([Enum]::Parse($tKind, 'Highlight'), [int]1))
$subrayado.Color = [System.Drawing.Color]::FromArgb(255, 214, 64)
$subrayado.Opacity = [float]0.45
$subrayado.Author = 'QA'
$subrayado.Area = New-Object System.Drawing.RectangleF([float]70, [float]52, [float]230, [float]16)
$lote.Add($subrayado)

# Nota anclada
$nota = [Activator]::CreateInstance($tItem, @([Enum]::Parse($tKind, 'Note'), [int]1))
$nota.Color = [System.Drawing.Color]::FromArgb(80, 140, 220)
$nota.Contents = 'Revisar esta partida con el aparejador.'
$nota.Author = 'QA'
$nota.Area = New-Object System.Drawing.RectangleF([float]420, [float]90, [float]20, [float]20)
$lote.Add($nota)

Comprobar 'Se acumulan tres marcas en memoria' ($lote.Items.Count -eq 3) "eran $($lote.Items.Count)"

# --- escribir ---
$info = New-Object System.IO.FileInfo($origen)
$identity = [Activator]::CreateInstance(
    $tIdentity, @([string]$origen, [long]$info.Length, [long]$info.LastWriteTimeUtc.Ticks))
$destino = Join-Path $trabajo 'anotado.pdf'
$save = $tService.GetMethod('Save', $flags)
$resultado = $save.Invoke($null, [object[]]@([string]$origen, [string]$destino, $lote, $identity))

Comprobar 'El original no se toca' (
    (New-Object System.IO.FileInfo($origen)).Length -eq $info.Length) 'cambio de tamano'
Comprobar 'Se escriben las tres anotaciones' (
    $resultado.AnnotationCount -eq 3) "escribio $($resultado.AnnotationCount)"

# --- verificar la estructura ---
$reader = New-Object iTextSharp.text.pdf.PdfReader($destino)
try {
    $pagina = $reader.GetPageN(1)
    $annots = $pagina.GetAsArray((New-Object iTextSharp.text.pdf.PdfName('Annots')))
    Comprobar 'La pagina declara tres anotaciones' (
        $annots -and $annots.Size -eq 3) "encontradas: $(if($annots){$annots.Size}else{0})"

    $subtipos = @()
    $conAutor = 0
    $conFecha = 0
    for ($i = 0; $i -lt $annots.Size; $i++) {
        $a = $annots.GetAsDict($i)
        $subtipos += $a.Get((New-Object iTextSharp.text.pdf.PdfName('Subtype'))).ToString()
        if ($a.Get((New-Object iTextSharp.text.pdf.PdfName('T')))) { $conAutor++ }
        if ($a.Get((New-Object iTextSharp.text.pdf.PdfName('M')))) { $conFecha++ }
    }

    Comprobar 'Hay un trazo de tinta (/Ink)' ($subtipos -contains '/Ink') ($subtipos -join ' ')
    Comprobar 'Hay un subrayado (/Highlight)' ($subtipos -contains '/Highlight') ($subtipos -join ' ')
    Comprobar 'Hay una nota (/Text)' ($subtipos -contains '/Text') ($subtipos -join ' ')
    Comprobar 'Todas llevan autor' ($conAutor -eq 3) "$conAutor de 3"
    Comprobar 'Todas llevan fecha' ($conFecha -eq 3) "$conFecha de 3"

    # El texto de la nota tiene que poder leerse
    $textoNota = $null
    for ($i = 0; $i -lt $annots.Size; $i++) {
        $a = $annots.GetAsDict($i)
        if ($a.Get((New-Object iTextSharp.text.pdf.PdfName('Subtype'))).ToString() -eq '/Text') {
            $c = $a.GetAsString((New-Object iTextSharp.text.pdf.PdfName('Contents')))
            if ($c) { $textoNota = $c.ToUnicodeString() }
        }
    }
    Comprobar 'La nota conserva su texto' (
        $textoNota -eq 'Revisar esta partida con el aparejador.') "leido: '$textoNota'"

    Comprobar 'El PDF sigue teniendo una pagina' ($reader.NumberOfPages -eq 1) "$($reader.NumberOfPages)"
}
finally { $reader.Close() }

# --- verificar que la aplicacion puede recuperarlas para dibujarlas ---
#
# PDFium NO dibuja anotaciones (comprobado escribiendo la apariencia de las dos
# formas posibles), y su version esta congelada en el proyecto. Por eso las
# marcas se guardan como anotaciones de verdad, para Acrobat y el movil, pero
# dentro de PDF Ligero las pinta la propia aplicacion leyendolas de vuelta.
$read = $tService.GetMethod('Read', $flags)
$recuperadas = $read.Invoke($null, [object[]]@([string]$destino))

Comprobar 'Se recuperan las tres marcas del PDF' (
    $recuperadas.Count -eq 3) "recupero $($recuperadas.Count)"

$tipos = @()
foreach ($m in $recuperadas) { $tipos += $m.Kind.ToString() }
Comprobar 'Vuelve el trazo' ($tipos -contains 'Ink') ($tipos -join ' ')
Comprobar 'Vuelve el subrayado' ($tipos -contains 'Highlight') ($tipos -join ' ')
Comprobar 'Vuelve la nota' ($tipos -contains 'Note') ($tipos -join ' ')

foreach ($m in $recuperadas) {
    if ($m.Kind.ToString() -eq 'Ink') {
        $puntos = 0
        foreach ($t in $m.Strokes) { $puntos += $t.Count }
        Comprobar 'El trazo conserva sus puntos' ($puntos -ge 20) "puntos: $puntos"
        Comprobar 'El trazo conserva su color' (
            $m.Color.R -eq 238 -and $m.Color.G -eq 91 -and $m.Color.B -eq 61) `
            "$($m.Color.R),$($m.Color.G),$($m.Color.B)"
    }
    if ($m.Kind.ToString() -eq 'Note') {
        Comprobar 'La nota recuperada conserva su texto' (
            $m.Contents -eq 'Revisar esta partida con el aparejador.') "'$($m.Contents)'"
    }
}

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'RESULTADO: PASS'
    exit 0
}
Write-Host "RESULTADO: FALLA ($fallos comprobaciones)"
exit 1
