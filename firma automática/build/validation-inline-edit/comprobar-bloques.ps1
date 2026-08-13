# Comprueba que se detectan las lineas de texto de una pagina con su
# tipografia, que es la base del editor sobre la pagina.
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

$tLocator = $exe.GetType('FirmaAutomatica.PdfTextBlockLocator')
$locate = $tLocator.GetMethod('Locate', [Reflection.BindingFlags]'Static,Public,NonPublic')

$fallos = 0
function Comprobar {
    param([string]$titulo, [bool]$ok, [string]$detalle)
    if ($ok) { Write-Host ("  OK    {0}" -f $titulo) }
    else { Write-Host ("  FALLA {0}  ->  {1}" -f $titulo, $detalle); $script:fallos++ }
}

$reader = New-Object iTextSharp.text.pdf.PdfReader($Pdf)
try {
    $esperado = @('Calibri', 'Calibri', 'TimesNewRoman', 'Arial', 'Consolas')

    for ($p = 1; $p -le 5; $p++) {
        $bloques = $locate.Invoke($null, [object[]]@($reader.PSObject.BaseObject, [int]$p))
        Write-Host ("--- pagina {0}: {1} lineas" -f $p, $bloques.Count)

        Comprobar "La pagina $p tiene lineas" ($bloques.Count -gt 0) 'ninguna'
        if ($bloques.Count -eq 0) { continue }

        $primera = $bloques[0]
        $texto = $primera.Text
        $recorte = $texto.Substring(0, [Math]::Min(52, $texto.Length))
        $b = $primera.Bounds
        Write-Host ("      '{0}'" -f $recorte)
        Write-Host ("      recuadro: x {0:N1} y {1:N1} ancho {2:N1} alto {3:N1}  fuente {4} {5:N1} pt" -f `
            $b.X, $b.Y, $b.Width, $b.Height,
            $primera.Style.FontName, $primera.Style.FontSizePoints)

        Comprobar "La linea de la pagina $p empieza por el texto esperado" (
            $texto.StartsWith('Texto de muestra')) "'$recorte'"
        Comprobar "La fuente de la pagina $p es la correcta" (
            $primera.Style.FontName -eq $esperado[$p-1]) `
            "detecto $($primera.Style.FontName)"
        Comprobar "El recuadro de la pagina $p tiene superficie" (
            $b.Width -gt 10 -and $b.Height -gt 3) "$($b.Width) x $($b.Height)"

        # Las lineas deben venir de arriba abajo
        if ($bloques.Count -gt 1) {
            $ordenadas = $true
            for ($i = 1; $i -lt $bloques.Count; $i++) {
                if ($bloques[$i].BaselineY -gt $bloques[$i-1].BaselineY + 0.5) {
                    $ordenadas = $false
                }
            }
            Comprobar "Las lineas de la pagina $p vienen de arriba abajo" $ordenadas 'desordenadas'
        }
    }
}
finally { $reader.Close() }

Write-Host ''
if ($fallos -eq 0) {
    Write-Host 'RESULTADO: PASS'
    exit 0
}
Write-Host "RESULTADO: FALLA ($fallos comprobaciones)"
exit 1
