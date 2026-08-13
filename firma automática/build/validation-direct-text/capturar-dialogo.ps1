# Abre el dialogo de edicion con la sustitucion real disponible y lo captura,
# para revisar que el titulo y el aviso dicen lo que va a pasar de verdad.
param([string]$Salida = (Join-Path $PSScriptRoot '..\output'))

$ErrorActionPreference = 'Stop'
$salidaResuelta = (Resolve-Path -LiteralPath $Salida).Path
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$exe = [Reflection.Assembly]::LoadFrom((Join-Path $salidaResuelta 'PDFLigero.exe'))

$tState = $exe.GetType('FirmaAutomatica.PdfTextEditDialogState')
$tDialog = $exe.GetType('FirmaAutomatica.PdfTextEditDialog')

$trabajo = Join-Path $PSScriptRoot 'trabajo'
New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

function Capturar {
    param([bool]$sustituir, [string]$nombre)

    $estado = [Activator]::CreateInstance($tState)
    $estado.Text = 'Presupuesto de ejecucion material'
    $estado.DetectedFontName = 'Calibri'
    $estado.DetectedDescription = 'Calibri  11 pt - negrita'
    $estado.BaseFontName = 'Calibri'
    $estado.Bold = $true
    $estado.FontSizePoints = [decimal]11
    $estado.AutoFit = $false
    $estado.CanReplaceInPlace = $true
    $estado.ReplaceInPlace = $sustituir
    $estado.ReplaceInPlaceReason = 'Se sustituye conservando su misma fuente.'

    $ctor = $tDialog.GetConstructors() | Where-Object {
        $_.GetParameters().Count -eq 2 } | Select-Object -First 1
    $dialogo = $ctor.Invoke(@($estado, [string]'PÁGINA 1'))
    try {
        $dialogo.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
        $dialogo.Location = New-Object System.Drawing.Point(40, 40)
        $dialogo.Show()
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 700
        [System.Windows.Forms.Application]::DoEvents()

        $bmp = New-Object System.Drawing.Bitmap($dialogo.Width, $dialogo.Height)
        $dialogo.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(
            0, 0, $dialogo.Width, $dialogo.Height)))
        $destino = Join-Path $trabajo $nombre
        $bmp.Save($destino)
        $bmp.Dispose()
        Write-Host "Captura: $destino"
    }
    finally {
        $dialogo.Close()
        $dialogo.Dispose()
    }
}

Capturar $true 'dialogo-sustituir.png'
Capturar $false 'dialogo-cubrir.png'
