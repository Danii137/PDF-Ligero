# Abre la aplicacion con un PDF, activa la herramienta de anotacion con su
# atajo y captura la ventana, para revisar la barra flotante.
param(
    [string]$Salida = (Join-Path $PSScriptRoot '..\output'),
    [int]$EsperaSegundos = 18
)

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $salidaResuelta 'itextsharp.dll')

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Ventana {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@

$trabajo = Join-Path $PSScriptRoot 'interfaz'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

# PDF de prueba con varias lineas
$pdf = Join-Path $trabajo 'plano.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    foreach ($i in 1..12) {
        $doc.Add((New-Object iTextSharp.text.Paragraph(
            "Linea $i de la memoria del proyecto, para poder subrayar y anotar."))) | Out-Null
    }
    $doc.Close()
}
finally { $fs.Dispose() }

$exe = Join-Path $salidaResuelta 'PDFLigero.exe'
$proc = Start-Process -FilePath $exe -ArgumentList @("--open", "`"$pdf`"") -PassThru
Start-Sleep -Seconds $EsperaSegundos

$proc.Refresh()
if ($proc.HasExited) {
    throw "La aplicacion se cerro sola (codigo $($proc.ExitCode))."
}

$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'La ventana principal no aparecio.' }

[Ventana]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900

# Activar la anotacion con el atajo
[System.Windows.Forms.SendKeys]::SendWait('^+a')
Start-Sleep -Seconds 3

$r = New-Object Ventana+RECT
[void][Ventana]::GetWindowRect($h, [ref]$r)
$ancho = $r.R - $r.L
$alto = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($ancho, $alto)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()
$destino = Join-Path $trabajo 'anotando.png'
$bmp.Save($destino)
$bmp.Dispose()

Write-Host "Captura: $destino"
Write-Host "Proceso vivo: $(-not $proc.HasExited)"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
