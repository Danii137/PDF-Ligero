# Genera con Word un PDF de muestra con una tipografia distinta por pagina, para
# comprobar que el detector devuelve la que realmente tiene cada texto.
#
# Se usa Word y no iTextSharp a proposito: interesa el caso real, con fuentes
# incrustadas en subconjuntos ("ABCDEF+Calibri"), que es lo que llega al estudio.
param(
    [string]$Destino = (Join-Path $PSScriptRoot 'muestras')
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $Destino) {
    [System.IO.Directory]::Delete($Destino, $true)
}
New-Item -ItemType Directory -Force -Path $Destino | Out-Null

$paginas = @(
    [pscustomobject]@{ Fuente = 'Calibri';         Tamano = 11.0; Negrita = $false; Cursiva = $false; Color = 0 },
    [pscustomobject]@{ Fuente = 'Calibri';         Tamano = 16.0; Negrita = $true;  Cursiva = $false; Color = 0 },
    [pscustomobject]@{ Fuente = 'Times New Roman'; Tamano = 12.0; Negrita = $false; Cursiva = $true;  Color = 0 },
    [pscustomobject]@{ Fuente = 'Arial';           Tamano = 9.5;  Negrita = $false; Cursiva = $false; Color = 8421504 },
    [pscustomobject]@{ Fuente = 'Consolas';        Tamano = 10.0; Negrita = $false; Cursiva = $false; Color = 0 }
)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    $doc = $word.Documents.Add()

    for ($i = 0; $i -lt $paginas.Count; $i++) {
        $p = $paginas[$i]

        $rango = $doc.Content
        $rango.Collapse(0)   # wdCollapseEnd

        if ($i -gt 0) {
            $rango.InsertBreak(7)   # wdPageBreak
            $rango = $doc.Content
            $rango.Collapse(0)
        }

        $texto = ("Texto de muestra en {0} de {1} puntos. " -f $p.Fuente, $p.Tamano) * 8
        $inicio = $rango.End
        $rango.InsertAfter($texto)

        $formato = $doc.Range($inicio, $doc.Content.End)
        $formato.Font.Name = [string]$p.Fuente
        $formato.Font.Size = [float]$p.Tamano
        $formato.Font.Bold = [int][bool]$p.Negrita
        $formato.Font.Italic = [int][bool]$p.Cursiva
        $formato.Font.Color = [int]$p.Color
    }

    $pdf = Join-Path $Destino 'muestras.pdf'
    $doc.SaveAs2([string]$pdf, 17)
    $doc.Close(0)

    Write-Host "PDF generado: $pdf"
    Write-Host 'Paginas esperadas:'
    for ($i = 0; $i -lt $paginas.Count; $i++) {
        $p = $paginas[$i]
        $estilo = @()
        if ($p.Negrita) { $estilo += 'negrita' }
        if ($p.Cursiva) { $estilo += 'cursiva' }
        $sufijo = if ($estilo.Count) { ' ' + ($estilo -join '+') } else { '' }
        Write-Host ("  {0}. {1} {2} pt{3}" -f ($i + 1), $p.Fuente, $p.Tamano, $sufijo)
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
