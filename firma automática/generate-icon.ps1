param(
    [string]$InputPngPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "assets\PDFLigero.png"),
    [string]$OutputIcoPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "build\output\PDFLigero.ico")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputPngPath)) {
    throw "No existe el PNG de origen: $InputPngPath"
}

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[object]

$sourceImage = [System.Drawing.Image]::FromFile($InputPngPath)
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                $targetWidth = [Math]::Max(1, [int][Math]::Round($size * 0.92))
                $targetHeight = [Math]::Max(1, [int][Math]::Round($size * 0.92))
                $ratio = [Math]::Min($targetWidth / $sourceImage.Width, $targetHeight / $sourceImage.Height)
                $drawWidth = [Math]::Max(1, [int][Math]::Round($sourceImage.Width * $ratio))
                $drawHeight = [Math]::Max(1, [int][Math]::Round($sourceImage.Height * $ratio))
                $left = [int][Math]::Round(($size - $drawWidth) / 2.0)
                $top = [int][Math]::Round(($size - $drawHeight) / 2.0)

                $graphics.DrawImage($sourceImage, (New-Object System.Drawing.Rectangle($left, $top, $drawWidth, $drawHeight)))
            }
            finally {
                $graphics.Dispose()
            }

            $stream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add([pscustomobject]@{
                    Size = $size
                    Bytes = $stream.ToArray()
                }) | Out-Null
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$outputDirectory = Split-Path -Parent $OutputIcoPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$iconStream = New-Object System.IO.FileStream($OutputIcoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try {
    $writer = New-Object System.IO.BinaryWriter($iconStream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)

        $offset = 6 + ($frames.Count * 16)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -ge 256) { 0 } else { [byte]$frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $iconStream.Dispose()
}

Write-Host "Icono generado en $OutputIcoPath"
