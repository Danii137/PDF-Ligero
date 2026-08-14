# Recorre el camino real de imprimir y captura cada paso, para ver donde
# aparece el cuadro de opciones nuevo.
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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class V3 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    static extern int GetWindowTextU(IntPtr h, StringBuilder s, int n);
    delegate bool EnumProc(IntPtr h, IntPtr p);

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(250);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    public static List<string> Ventanas() {
        var r = new List<string>();
        EnumWindows((h, p) => {
            if (!IsWindowVisible(h)) { return true; }
            var t = new StringBuilder(300);
            GetWindowTextU(h, t, 300);
            if (t.Length > 0) { r.Add(h.ToInt64() + "|" + t); }
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
'@

$trabajo = Join-Path $PSScriptRoot 'flujo'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

$pdf = Join-Path $trabajo 'memoria.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    foreach ($i in 1..6) {
        $doc.Add((New-Object iTextSharp.text.Paragraph("Pagina $i de la memoria."))) | Out-Null
        if ($i -lt 6) { $doc.NewPage() | Out-Null }
    }
    $doc.Close()
}
finally { $fs.Dispose() }

$exe = Join-Path $salidaResuelta 'PDFLigero.exe'
$proc = Start-Process -FilePath $exe -ArgumentList @('--open', "`"$pdf`"") -PassThru
Start-Sleep -Seconds $EsperaSegundos
$proc.Refresh()
if ($proc.HasExited) { throw "Se cerro sola ($($proc.ExitCode))." }

[V3]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

function CapturarVentana {
    param([long]$handle, [string]$nombre)
    $r = New-Object V3+RECT
    [void][V3]::GetWindowRect([IntPtr]$handle, [ref]$r)
    $w = $r.R - $r.L
    $h = $r.B - $r.T
    if ($w -lt 50 -or $h -lt 50) { return $null }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $destino = Join-Path $trabajo $nombre
    $bmp.Save($destino)
    $bmp.Dispose()
    Write-Host "Captura: $destino"
    return $r
}

# Paso 1: Ctrl+P abre la vista previa
[System.Windows.Forms.SendKeys]::SendWait('^p')
Start-Sleep -Seconds 4

$previa = [V3]::Ventanas() | Where-Object { $_ -match '\|Imprimir$' } | Select-Object -First 1
if (-not $previa) {
    Write-Host 'No se encontro la vista previa. Ventanas visibles:'
    [V3]::Ventanas() | Select-Object -First 12 | ForEach-Object { "   $_" }
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

$handlePrevia = [long]($previa -split '\|')[0]
Write-Host ("Ventana 1: {0}" -f ($previa -split '\|')[1])
$rPrevia = CapturarVentana $handlePrevia 'paso1-opciones.png'

# Paso 2: el boton Imprimir de la vista previa, abajo a la derecha
$x = $rPrevia.R - 90
$y = $rPrevia.B - 40
[V3]::Click($x, $y)
Start-Sleep -Seconds 3

$opciones = [V3]::Ventanas() | Where-Object { $_ -match '\|Imprimir$' } | Select-Object -First 1
if ($opciones) {
    $handleOpciones = [long]($opciones -split '\|')[0]
    Write-Host ("Ventana 2: {0}" -f ($opciones -split '\|')[1])
    CapturarVentana $handleOpciones 'paso2-opciones.png' | Out-Null
} else {
    Write-Host 'No aparecio el cuadro de opciones. Ventanas visibles:'
    [V3]::Ventanas() | Select-Object -First 12 | ForEach-Object { "   $_" }
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
