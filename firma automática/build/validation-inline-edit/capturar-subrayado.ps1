# Comprueba en la aplicacion real que el subrayador sigue al texto: se arrastra
# desde la mitad de una linea hasta la mitad de otra y debe marcar solo el texto
# recorrido, no un cuadro que englobe los margenes.
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

public static class Raton2 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);

    public static void Arrastrar(int x1, int y1, int x2, int y2) {
        SetCursorPos(x1, y1);
        System.Threading.Thread.Sleep(250);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        for (int i = 1; i <= 14; i++) {
            SetCursorPos(x1 + ((x2 - x1) * i / 14), y1 + ((y2 - y1) * i / 14));
            System.Threading.Thread.Sleep(45);
        }
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$trabajo = Join-Path $PSScriptRoot 'subrayado'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

$pdf = Join-Path $trabajo 'memoria.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    $fuente = New-Object iTextSharp.text.Font(
        [iTextSharp.text.Font+FontFamily]::HELVETICA, 12)
    foreach ($t in @(
        'La presente memoria describe las obras de reforma interior',
        'del local situado en la calle Mayor numero once, con destino',
        'a oficina de trabajo, segun el proyecto redactado al efecto',
        'por el arquitecto que suscribe el presente documento.')) {
        $par = New-Object iTextSharp.text.Paragraph($t, $fuente)
        $par.SpacingAfter = 10
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

[Raton2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800

# Activar la anotacion y elegir el subrayador (segundo boton de la barra)
[System.Windows.Forms.SendKeys]::SendWait('^+a')
Start-Sleep -Seconds 3

$r = New-Object Raton2+RECT
[void][Raton2]::GetWindowRect($h, [ref]$r)
$ancho = $r.R - $r.L
$alto = $r.B - $r.T

function Capturar {
    param([string]$nombre)
    $bmp = New-Object System.Drawing.Bitmap($ancho, $alto)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $destino = Join-Path $trabajo $nombre
    $bmp.Save($destino)
    $bmp.Dispose()
    Write-Host "Captura: $destino"
}

# El subrayador es el segundo boton de la barra flotante. La barra se coloca
# dentro del visor, que no ocupa toda la ventana: se descuentan el panel de
# navegacion de la izquierda y el rail de herramientas de la derecha. Estas
# coordenadas estan medidas sobre una captura real.
$barraX = $r.L + 846
$barraY = $r.T + 151
[Raton2]::Click($barraX, $barraY)
Start-Sleep -Seconds 1

# Arrastrar desde la mitad de la primera linea hasta la mitad de la tercera
$x1 = $r.L + [int]($ancho * 0.40)
$y1 = $r.T + [int]($alto * 0.243)
$x2 = $r.L + [int]($ancho * 0.36)
$y2 = $r.T + [int]($alto * 0.305)
[Raton2]::Arrastrar($x1, $y1, $x2, $y2)
Start-Sleep -Seconds 2
Capturar 'subrayado-seleccion.png'

Write-Host "Proceso vivo: $(-not $proc.HasExited)"
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
