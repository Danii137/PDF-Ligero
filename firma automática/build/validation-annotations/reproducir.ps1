# Reproduce el guardado de un subrayado sobre un PDF real, sin pasar por la
# interfaz, para ver la excepcion con su pila completa.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output'),
    [string]$Pdf
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

if (-not $Pdf -or -not (Test-Path -LiteralPath $Pdf)) {
    throw "Indica un PDF existente con -Pdf"
}
$Pdf = (Resolve-Path -LiteralPath $Pdf).Path
Write-Host "PDF: $Pdf"

$tItem = $exe.GetType('FirmaAutomatica.PdfAnnotationItem')
$tBatch = $exe.GetType('FirmaAutomatica.PdfAnnotationBatch')
$tService = $exe.GetType('FirmaAutomatica.PdfAnnotationService')
$tKind = $exe.GetType('FirmaAutomatica.PdfAnnotationKind')
$tIdentity = $exe.GetType('FirmaAutomatica.PdfEditViewIdentity')
$tLocator = $exe.GetType('FirmaAutomatica.PdfTextBlockLocator')
$bf = [Reflection.BindingFlags]'Static,Public,NonPublic'

# Localizar una linea de texto real de la primera pagina
$reader = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
try {
    $locate = $tLocator.GetMethod('Locate', $bf)
    $bloques = $locate.Invoke($null, [object[]]@($reader.PSObject.BaseObject, [int]1))
    Write-Host "Lineas encontradas en la pagina 1: $($bloques.Count)"
}
finally { $reader.Close() }

if ($bloques.Count -eq 0) { throw 'La pagina 1 no tiene lineas de texto.' }

$linea = $bloques[0]
Write-Host ("Linea elegida: '{0}'" -f $linea.Text)

# Subrayado que sigue a esa linea, como hace la herramienta
$lote = [Activator]::CreateInstance($tBatch)
$marca = [Activator]::CreateInstance($tItem, @([Enum]::Parse($tKind, 'Highlight'), [int]1))
$marca.Color = [System.Drawing.Color]::FromArgb(255, 214, 64)
$marca.Opacity = [float]0.4
$marca.Author = 'QA'
$tramo = $linea.SpanBounds(0, $linea.CharacterBounds.Count)
$marca.Quads.Add($tramo)
$marca.Area = $marca.GetBounds()
$lote.Add($marca)
Write-Host ("Tramos del subrayado: {0}" -f $marca.Quads.Count)

$info = New-Object System.IO.FileInfo($Pdf)
$identity = [Activator]::CreateInstance(
    $tIdentity, @([string]$Pdf, [long]$info.Length, [long]$info.LastWriteTimeUtc.Ticks))
$destino = Join-Path $env:TEMP ('repro-' + [Guid]::NewGuid().ToString('N') + '.pdf')

try {
    $save = $tService.GetMethod('Save', $bf)
    $r = $save.Invoke($null, [object[]]@([string]$Pdf, [string]$destino, $lote, $identity))
    Write-Host ("OK: se escribieron {0} anotaciones" -f $r.AnnotationCount)
}
catch {
    $inner = $_.Exception
    while ($inner.InnerException) { $inner = $inner.InnerException }
    Write-Host ''
    Write-Host '=== EXCEPCION CON SU PILA ==='
    Write-Host $inner.GetType().FullName
    Write-Host $inner.Message
    Write-Host $inner.StackTrace
}
finally {
    if (Test-Path -LiteralPath $destino) { Remove-Item -LiteralPath $destino -Force }
}
