# Abre la aplicacion, activa la edicion de texto y pincha una linea, para
# comprobar que los recuadros, el cuadro de escritura y la barra de formato
# aparecen donde deben.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output'),
    [int]$EsperaSegundos = 16
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Raton {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(220);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
    public static void Mover(int x, int y) { SetCursorPos(x, y); }
}
'@

$trabajo = Join-Path $PSScriptRoot 'interfaz'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

# PDF con lineas bien separadas y facilmente identificables
$pdf = Join-Path $trabajo 'memoria.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    $fuente = New-Object iTextSharp.text.Font(
        [iTextSharp.text.Font+FontFamily]::HELVETICA, 13)
    foreach ($t in @(
        'MEMORIA DESCRIPTIVA',
        'Promotor: Agoin Estate Group SL',
        'Emplazamiento: calle Mayor numero once',
        'Presupuesto de ejecucion material',
        'Superficie construida total')) {
        $par = New-Object iTextSharp.text.Paragraph($t, $fuente)
        $par.SpacingAfter = 14
        $doc.Add($par) | Out-Null
    }
    $doc.Close()
}
finally { $fs.Dispose() }

$exe = Join-Path $salidaResuelta 'PDFLigero.exe'
$proc = Start-Process -FilePath $exe -ArgumentList @('--open', "`"$pdf`"") -PassThru
Start-Sleep -Seconds $EsperaSegundos

$proc.Refresh()
if ($proc.HasExited) { throw "La aplicacion se cerro sola ($($proc.ExitCode))." }
$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'No aparecio la ventana.' }

[Raton]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800

# Activar la edicion de texto
[System.Windows.Forms.SendKeys]::SendWait('^+e')
Start-Sleep -Seconds 3

$r = New-Object Raton+RECT
[void][Raton]::GetWindowRect($h, [ref]$r)

function Capturar {
    param([string]$nombre)
    $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $destino = Join-Path $trabajo $nombre
    $bmp.Save($destino)
    $bmp.Dispose()
    Write-Host "Captura: $destino"
}

# Pasar el raton sobre una linea para ver el resaltado
# Centro de la linea "Promotor: ..." medida sobre la captura anterior
$x = $r.L + [int](($r.R - $r.L) * 0.33)
$y = $r.T + [int](($r.B - $r.T) * 0.304)
[Raton]::Mover($x, $y)
Start-Sleep -Seconds 2
Capturar 'editor-recuadros.png'

# Pinchar la linea para escribir encima
[Raton]::Click($x, $y)
Start-Sleep -Seconds 3
Capturar 'editor-escribiendo.png'

# Escribir un texto nuevo y aplicarlo
[System.Windows.Forms.SendKeys]::SendWait('^a')
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('Promotor: Estudio Agoin Arquitectura')
Start-Sleep -Milliseconds 600
Capturar 'editor-texto-nuevo.png'

[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 6
Capturar 'editor-aplicado.png'

Write-Host "Proceso vivo: $(-not $proc.HasExited)"
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
