# QA del cuadro de impresion: seleccion de paginas y capacidades de impresora.
param([string]$Salida = (Join-Path $PSScriptRoot '..\output'))

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

$tParser = $exe.GetType('FirmaAutomatica.PdfPageRangeParser')
$tKind = $exe.GetType('FirmaAutomatica.PdfPageSelectionKind')
$bf = [Reflection.BindingFlags]'Static,Public,NonPublic'
$resolve = $tParser.GetMethod('Resolve', $bf)

$fallos = 0
function Comprobar {
    param([string]$titulo, [bool]$ok, [string]$detalle)
    if ($ok) { Write-Host ("  OK    {0}" -f $titulo) }
    else { Write-Host ("  FALLA {0}  ->  {1}" -f $titulo, $detalle); $script:fallos++ }
}

function Paginas {
    param([string]$modo, [string]$texto, [int]$total, [int]$actual)
    $k = [Enum]::Parse($tKind, $modo)
    $r = $resolve.Invoke($null, [object[]]@($k, [string]$texto, [int]$total, [int]$actual))
    return ($r -join ',')
}

Write-Host '=== seleccion de paginas ==='
Comprobar 'Todas' ((Paginas 'All' '' 6 1) -eq '1,2,3,4,5,6') (Paginas 'All' '' 6 1)
Comprobar 'Solo la actual' ((Paginas 'Current' '' 6 4) -eq '4') (Paginas 'Current' '' 6 4)
Comprobar 'Solo impares' ((Paginas 'Odd' '' 6 1) -eq '1,3,5') (Paginas 'Odd' '' 6 1)
Comprobar 'Solo pares' ((Paginas 'Even' '' 6 1) -eq '2,4,6') (Paginas 'Even' '' 6 1)
Comprobar 'Intervalo simple' ((Paginas 'Range' '2-4' 6 1) -eq '2,3,4') (Paginas 'Range' '2-4' 6 1)
Comprobar 'Intervalo compuesto' (
    (Paginas 'Range' '1-2, 5, 6' 6 1) -eq '1,2,5,6') (Paginas 'Range' '1-2, 5, 6' 6 1)
Comprobar 'Una sola pagina' ((Paginas 'Range' '3' 6 1) -eq '3') (Paginas 'Range' '3' 6 1)
Comprobar 'Intervalo al reves' ((Paginas 'Range' '5-2' 6 1) -eq '2,3,4,5') (Paginas 'Range' '5-2' 6 1)
Comprobar 'Se recorta al total' ((Paginas 'Range' '4-99' 6 1) -eq '4,5,6') (Paginas 'Range' '4-99' 6 1)
Comprobar 'No se repiten' ((Paginas 'Range' '2, 2-3, 3' 6 1) -eq '2,3') (Paginas 'Range' '2, 2-3, 3' 6 1)
Comprobar 'Texto sin sentido no imprime nada' (
    (Paginas 'Range' 'hola' 6 1) -eq '') (Paginas 'Range' 'hola' 6 1)
Comprobar 'Fuera de rango no imprime nada' (
    (Paginas 'Range' '99' 6 1) -eq '') (Paginas 'Range' '99' 6 1)

Write-Host ''
Write-Host '=== impresoras del equipo y lo que admiten ==='
foreach ($nombre in [System.Drawing.Printing.PrinterSettings]::InstalledPrinters) {
    $s = New-Object System.Drawing.Printing.PrinterSettings
    $s.PrinterName = $nombre
    try {
        Write-Host ("  {0,-34} color:{1,-6} doble cara:{2,-6} papeles:{3}" -f `
            $nombre, $s.SupportsColor, $s.CanDuplex, $s.PaperSizes.Count)
    }
    catch {
        Write-Host ("  {0,-34} (no responde)" -f $nombre)
    }
}

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'RESULTADO: PASS'
    exit 0
}
Write-Host "RESULTADO: FALLA ($fallos comprobaciones)"
exit 1
