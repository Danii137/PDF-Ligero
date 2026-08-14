# Compara la misma linea antes y durante la edicion, recortando solo esa zona,
# para ver si el texto cambia de tamano al pincharlo.
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

public static class V4 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(260);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$trabajo = Join-Path $PSScriptRoot 'tamano'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

$pdf = Join-Path $trabajo 'memoria.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    $f = New-Object iTextSharp.text.Font([iTextSharp.text.Font+FontFamily]::HELVETICA, 14)
    foreach ($t in @(
        'Promotor del proyecto de reforma',
        'Emplazamiento de las obras',
        'Presupuesto de ejecucion material')) {
        $par = New-Object iTextSharp.text.Paragraph($t, $f)
        $par.SpacingAfter = 18
        $doc.Add($par) | Out-Null
    }
    $doc.Close()
}
finally { $fs.Dispose() }

$exe = Join-Path $salidaResuelta 'PDFLigero.exe'
$proc = Start-Process -FilePath $exe -ArgumentList @('--open', "`"$pdf`"") -PassThru
Start-Sleep -Seconds $EsperaSegundos
$proc.Refresh()
if ($proc.HasExited) { throw "Se cerro sola." }

$h = $proc.MainWindowHandle
[V4]::ShowWindow($h, 3) | Out-Null   # maximizada, para que quede delante
Start-Sleep -Milliseconds 600
[V4]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 1

$r = New-Object V4+RECT
[void][V4]::GetWindowRect([IntPtr]$h, [ref]$r)
$ancho = $r.R - $r.L
$alto = $r.B - $r.T

# Recorte alrededor de la primera linea de texto
$zonaX = $r.L + [int]($ancho * 0.20)
$zonaY = $r.T + [int]($alto * 0.19)
$zonaW = [int]($ancho * 0.42)
$zonaH = [int]($alto * 0.10)

function Recortar {
    param([string]$nombre)
    $bmp = New-Object System.Drawing.Bitmap($zonaW, $zonaH)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($zonaX, $zonaY, 0, 0, $bmp.Size)
    $g.Dispose()
    $destino = Join-Path $trabajo $nombre
    $bmp.Save($destino)
    $bmp.Dispose()
    Write-Host "Captura: $destino"
}

# Activar la edicion y capturar antes de pinchar
[System.Windows.Forms.SendKeys]::SendWait('^+e')
Start-Sleep -Seconds 3
Recortar 'antes.png'

# Pinchar la primera linea
[V4]::Click(($zonaX + [int]($zonaW * 0.35)), ($zonaY + [int]($zonaH * 0.45)))
Start-Sleep -Seconds 3
Recortar 'durante.png'

Write-Host "Proceso vivo: $(-not $proc.HasExited)"
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
