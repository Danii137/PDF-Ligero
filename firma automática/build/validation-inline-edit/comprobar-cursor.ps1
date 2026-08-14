# Comprueba el cursor real del sistema con el raton sobre el texto del PDF,
# no sobre las barras de herramientas.
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

public static class V5 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO {
        public int cbSize; public int flags; public IntPtr hCursor; public int x; public int y;
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr h, int id);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);

    public static IntPtr CursorActual() {
        var ci = new CURSORINFO();
        ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
        GetCursorInfo(ref ci);
        return ci.hCursor;
    }

    // Identificadores estandar de Windows
    public static IntPtr Flecha() { return LoadCursor(IntPtr.Zero, 32512); }
    public static IntPtr Texto()  { return LoadCursor(IntPtr.Zero, 32513); }
    public static IntPtr Cruz()   { return LoadCursor(IntPtr.Zero, 32515); }
    public static IntPtr Mano()   { return LoadCursor(IntPtr.Zero, 32649); }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(260);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    public static void Mover(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        SetCursorPos(x + 1, y);
        System.Threading.Thread.Sleep(400);
    }
}
'@

$trabajo = Join-Path $PSScriptRoot 'cursor'
if (Test-Path -LiteralPath $trabajo) { [System.IO.Directory]::Delete($trabajo, $true) }
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

$pdf = Join-Path $trabajo 'memoria.pdf'
$doc = New-Object iTextSharp.text.Document
$fs = New-Object System.IO.FileStream($pdf, [System.IO.FileMode]::Create)
try {
    [iTextSharp.text.pdf.PdfWriter]::GetInstance($doc, $fs) | Out-Null
    $doc.Open()
    $f = New-Object iTextSharp.text.Font([iTextSharp.text.Font+FontFamily]::HELVETICA, 14)
    foreach ($t in @('Primera linea de la memoria', 'Segunda linea de la memoria')) {
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
if ($proc.HasExited) { throw 'Se cerro sola.' }

$h = $proc.MainWindowHandle
# Sin maximizar: la barra flotante se coloca respecto al visor, y estas
# coordenadas estan medidas con la ventana en su tamano normal.
[V5]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 1

$r = New-Object V5+RECT
[void][V5]::GetWindowRect([IntPtr]$h, [ref]$r)
$ancho = $r.R - $r.L
$alto = $r.B - $r.T

# Punto sobre el texto del PDF, lejos de cualquier barra
$x = $r.L + [int]($ancho * 0.34)
$y = $r.T + [int]($alto * 0.20)

$flecha = [V5]::Flecha()
$texto = [V5]::Texto()
$cruz = [V5]::Cruz()
$mano = [V5]::Mano()

function Nombre {
    param([IntPtr]$c)
    if ($c -eq $texto) { return 'texto (I)' }
    if ($c -eq $cruz) { return 'cruz' }
    if ($c -eq $mano) { return 'MANO' }
    if ($c -eq $flecha) { return 'flecha' }
    return 'otro'
}

$fallos = 0
function Comprobar {
    param([string]$titulo, [bool]$ok, [string]$detalle)
    if ($ok) { Write-Host ("  OK    {0}" -f $titulo) }
    else { Write-Host ("  FALLA {0}  ->  {1}" -f $titulo, $detalle); $script:fallos++ }
}

# 1. Anotar con el rotulador: cruz
[System.Windows.Forms.SendKeys]::SendWait('^+a')
Start-Sleep -Seconds 3
[V5]::Mover($x, $y)
$c = [V5]::CursorActual()
Comprobar 'Rotulador sobre el texto: cruz' ($c -eq $cruz) (Nombre $c)

# 2. Cambiar al subrayador (segundo boton de la barra)
# Coordenadas del boton medidas sobre una captura real: la barra se
# coloca dentro del visor, que no ocupa toda la ventana.
[V5]::Click(($r.L + 846), ($r.T + 151))
Start-Sleep -Seconds 1
[V5]::Mover($x, $y)
$c = [V5]::CursorActual()
Comprobar 'Subrayador sobre el texto: cursor de texto' ($c -eq $texto) (Nombre $c)

# 3. Cerrar la anotacion y abrir la edicion
[System.Windows.Forms.SendKeys]::SendWait('^+a')
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('^+e')
Start-Sleep -Seconds 3
[V5]::Mover($x, $y)
$c = [V5]::CursorActual()
Comprobar 'Editar sobre una linea: cursor de texto' ($c -eq $texto) (Nombre $c)

Write-Host ''
if ($fallos -eq 0) { Write-Host 'RESULTADO: PASS' } else { Write-Host "RESULTADO: FALLA ($fallos)" }
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
exit $(if ($fallos -eq 0) { 0 } else { 1 })
