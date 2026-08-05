<#
.SYNOPSIS
    Compila Word2PDF.exe con el icono del plátano rojo de PDF Ligero.

.DESCRIPTION
    El ejecutable anterior se generaba sin icono, asi que Windows le ponia el de
    PyInstaller por defecto y no parecia la misma herramienta que PDF Ligero.

    El icono se genera desde el mismo PNG que usa PDF Ligero, de modo que no hay
    dos fuentes del logo que puedan desincronizarse.

    El EXE nuevo se deja primero en una carpeta de trabajo y solo sustituye al
    actual despues de comprobar que arranca y convierte de verdad. El anterior se
    conserva con la extension .bak.

.PARAMETER OmitirPrueba
    Sustituye el ejecutable sin ejecutar la conversion de prueba. Solo para
    cuando no hay Microsoft Word en el equipo.
#>
param(
    [switch]$OmitirPrueba
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$trabajo = Join-Path $root ".word2pdf-build"
$destino = Join-Path $root "Word2PDF.exe"

# La carpeta se llama "firma automática", con tilde. Escribirla en el script lo
# haria depender de que el host lea el archivo como UTF-8, asi que se localiza
# por comodin, igual que hace instalar.ps1.
$carpetaFirma = Get-ChildItem -LiteralPath $root -Directory -Filter "firma*" |
    Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName "generate-icon.ps1")
    } |
    Select-Object -First 1

if ($null -eq $carpetaFirma) {
    throw "No se encuentra la carpeta de PDF Ligero junto a este script."
}

$iconoOrigen = Join-Path $carpetaFirma.FullName "assets\PDFLigero.png"
$iconoIco = Join-Path $trabajo "PDFLigero.ico"
$generador = Join-Path $carpetaFirma.FullName "generate-icon.ps1"

# Homer va aparte del icono de la aplicacion: el platano identifica la
# herramienta en el Explorador y en el menu contextual, y Homer sale en la
# ventanita mientras se convierte. Si no esta el PNG, se compila sin el.
$homerOrigen = Join-Path $root "logo.png"
$homerIco = Join-Path $trabajo "homer.ico"

if (-not (Test-Path -LiteralPath $iconoOrigen)) {
    throw "No se encuentra el PNG del icono: $iconoOrigen"
}

New-Item -ItemType Directory -Force -Path $trabajo | Out-Null

Write-Host "[1/4] Generando los iconos..."
& $generador -InputPngPath $iconoOrigen -OutputIcoPath $iconoIco

$incluirHomer = Test-Path -LiteralPath $homerOrigen
if ($incluirHomer) {
    & $generador -InputPngPath $homerOrigen -OutputIcoPath $homerIco
}
else {
    Write-Host "      Sin logo.png: la ventanita usara el icono normal."
}

Write-Host "[2/4] Compilando con PyInstaller..."

# La receta vive en Word2PDF.spec, no aqui: hace falta un .spec para poder
# quitar binarios concretos del paquete, y todo lo que va dentro se descomprime
# en %TEMP% en cada ejecucion.
$receta = Join-Path $root "Word2PDF.spec"
if (-not (Test-Path -LiteralPath $receta)) {
    throw "No se encuentra la receta de empaquetado: $receta"
}

$argumentos = @(
    "-3", "-m", "PyInstaller",
    "--clean", "--noconfirm",
    "--workpath", (Join-Path $trabajo "pyi-work"),
    "--distpath", (Join-Path $trabajo "pyi-dist"),
    $receta
)

& py @argumentos
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller ha fallado."
}

$nuevo = Join-Path $trabajo "pyi-dist\Word2PDF.exe"
if (-not (Test-Path -LiteralPath $nuevo)) {
    throw "PyInstaller no genero el ejecutable esperado."
}

if (-not $OmitirPrueba) {
    Write-Host "[3/4] Probando una conversion real antes de sustituir..."
    $pruebaDir = Join-Path $trabajo "prueba"
    Remove-Item -LiteralPath $pruebaDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $pruebaDir | Out-Null

    $docx = [string](Join-Path $pruebaDir "prueba de conversion.docx")
    $word = New-Object -ComObject Word.Application
    try {
        $word.Visible = $false
        $word.DisplayAlerts = 0
        $documento = $word.Documents.Add()
        $documento.Content.Text =
            "Prueba de Word2PDF con el icono de PDF Ligero."
        # SaveAs2 con argumentos planos: SaveAs con [ref] falla al enlazar
        # tarde desde PowerShell. 16 es wdFormatDocumentDefault (.docx).
        $documento.SaveAs2($docx, 16)
        $documento.Close(0)
    }
    finally {
        $word.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }

    & $nuevo $docx | Out-Null
    $pdf = [System.IO.Path]::ChangeExtension($docx, ".pdf")
    if (-not (Test-Path -LiteralPath $pdf)) {
        throw "El ejecutable nuevo no convirtio el documento de prueba. No se sustituye el actual."
    }

    $tamano = (Get-Item -LiteralPath $pdf).Length
    if ($tamano -lt 1000) {
        throw "El PDF de prueba salio vacio ($tamano bytes). No se sustituye el actual."
    }

    Write-Host ("      PDF de prueba correcto: {0:N0} bytes" -f $tamano)
}
else {
    Write-Host "[3/4] Prueba de conversion omitida por peticion."
}

Write-Host "[4/4] Sustituyendo el ejecutable..."
if (Test-Path -LiteralPath $destino) {
    Copy-Item -LiteralPath $destino -Destination ($destino + ".bak") -Force
    Write-Host "      Copia del anterior en Word2PDF.exe.bak"
}

Copy-Item -LiteralPath $nuevo -Destination $destino -Force

Write-Host ""
Write-Host ("Listo: {0} ({1:N1} MiB)" -f $destino, ((Get-Item -LiteralPath $destino).Length / 1MB))
Write-Host "Ejecuta instalar.bat para que el Explorador use el icono nuevo."
