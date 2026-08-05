<#
.SYNOPSIS
    Comprueba si un pdfium.dll candidato es compatible con el wrapper
    PdfiumViewer que usa PDF Ligero.

.DESCRIPTION
    PdfiumViewer 2.13 llama por P/Invoke a un conjunto fijo de funciones de
    pdfium.dll. Si una compilacion mas moderna no exporta alguna de ellas, la
    aplicacion falla en tiempo de ejecucion, no al compilar, y solo en el
    momento exacto en que se usa esa funcion.

    Este script enumera lo que el wrapper necesita, lo compara con lo que el
    candidato exporta y dice ademas que parte de PDF Ligero se rompe con cada
    ausencia.

    Leer la tabla de exportaciones a mano evita depender de dumpbin, que no
    forma parte de Windows.

.PARAMETER CandidatePath
    Ruta del pdfium.dll que se quiere evaluar.

.EXAMPLE
    .\check-pdfium-compatibility.ps1 -CandidatePath C:\temp\pdfium.dll
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath
)

$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$wrapper = Join-Path `
    $root `
    "packages\PdfiumViewer.2.13.0.0\lib\net20\PdfiumViewer.dll"

if (-not (Test-Path -LiteralPath $wrapper)) {
    throw "No se encuentra PdfiumViewer.dll. Ejecuta antes build.ps1 sin -SkipRestore."
}

if (-not (Test-Path -LiteralPath $CandidatePath)) {
    throw "No se encuentra el pdfium.dll candidato: $CandidatePath"
}

# Las APIs de .NET usan el directorio del proceso, no la ubicacion de
# PowerShell, asi que una ruta relativa se resolveria en otro sitio.
$CandidatePath = (Resolve-Path -LiteralPath $CandidatePath).ProviderPath

# Consecuencia conocida de cada ausencia, para que el informe sea accionable.
# Se obtuvo rastreando el IL del wrapper: ver la seccion de PDFium en
# ..\..\..\CONTEXTO_PDF_LIGERO.md.
$impacto = @{
    "FPDF_AddRef" = "Declarada en el wrapper pero nunca llamada."
    "FPDF_Release" = "PdfLibrary.Dispose. Rompe la descarga limpia de la libreria."
    "FPDFDest_GetPageIndex" =
        "PdfFile.GetBookmarkPageIndex y PdfFile.GetPageLinks. Rompe la " +
        "navegacion por marcadores y los enlaces de pagina. Upstream se " +
        "renombro a FPDFDest_GetDestPageIndex."
    "FPDFPageObj_NewImgeObj" =
        "Declarada pero nunca llamada. Upstream corrigio la errata y se llama " +
        "FPDFPageObj_NewImageObj."
}

# Que una funcion falte no basta para descartar un candidato: el wrapper declara
# algunas que nunca invoca. De hecho el pdfium.dll que la aplicacion usa hoy
# tampoco exporta FPDFPageObj_NewImgeObj, y funciona. Solo bloquean las que
# alguien llega a llamar de verdad.
$inocuas = @("FPDF_AddRef", "FPDFPageObj_NewImgeObj")

function Get-PeExportNames {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $numberOfSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $sizeOfOptionalHeader = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeader = $peOffset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optionalHeader)

    # PE32+ situa el directorio de datos 16 bytes mas alla que PE32.
    $dataDirectory = if ($magic -eq 0x20B) {
        $optionalHeader + 112
    }
    else {
        $optionalHeader + 96
    }

    $exportRva = [BitConverter]::ToUInt32($bytes, $dataDirectory)
    $sectionTable = $optionalHeader + $sizeOfOptionalHeader
    $sections = @()
    for ($i = 0; $i -lt $numberOfSections; $i++) {
        $s = $sectionTable + ($i * 40)
        $sections += [PSCustomObject]@{
            VirtualAddress = [BitConverter]::ToUInt32($bytes, $s + 12)
            VirtualSize = [BitConverter]::ToUInt32($bytes, $s + 8)
            PointerToRawData = [BitConverter]::ToUInt32($bytes, $s + 20)
        }
    }

    function ConvertTo-FileOffset {
        param([uint32]$Rva)

        foreach ($s in $sections) {
            $size = [Math]::Max($s.VirtualSize, 1)
            if ($Rva -ge $s.VirtualAddress -and
                $Rva -lt ($s.VirtualAddress + $size)) {
                return $s.PointerToRawData + ($Rva - $s.VirtualAddress)
            }
        }

        return 0
    }

    $exportOffset = ConvertTo-FileOffset $exportRva
    if ($exportOffset -eq 0) {
        return @()
    }

    $numberOfNames = [BitConverter]::ToUInt32($bytes, $exportOffset + 24)
    $namesRva = [BitConverter]::ToUInt32($bytes, $exportOffset + 32)
    $namesOffset = ConvertTo-FileOffset $namesRva

    $names = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $numberOfNames; $i++) {
        $nameRva = [BitConverter]::ToUInt32($bytes, $namesOffset + ($i * 4))
        $offset = ConvertTo-FileOffset $nameRva
        if ($offset -eq 0) {
            continue
        }

        $end = $offset
        while ($bytes[$end] -ne 0) {
            $end++
        }

        $names.Add([System.Text.Encoding]::ASCII.GetString(
            $bytes,
            $offset,
            $end - $offset))
    }

    return $names
}

function Get-WrapperImportNames {
    param([string]$Path)

    $assembly = [Reflection.Assembly]::LoadFrom($Path)
    $flags = `
        [System.Reflection.BindingFlags]::Static -bor `
        [System.Reflection.BindingFlags]::Instance -bor `
        [System.Reflection.BindingFlags]::Public -bor `
        [System.Reflection.BindingFlags]::NonPublic
    $imports = New-Object System.Collections.Generic.HashSet[string]

    foreach ($type in $assembly.GetTypes()) {
        foreach ($method in $type.GetMethods($flags)) {
            $isPinvoke = ($method.Attributes -band
                [System.Reflection.MethodAttributes]::PinvokeImpl) -ne 0
            if (-not $isPinvoke) {
                continue
            }

            $attributes = $method.GetCustomAttributes(
                [System.Runtime.InteropServices.DllImportAttribute],
                $false)
            if ($attributes.Count -eq 0 -or
                $attributes[0].Value -notlike "*pdfium*") {
                continue
            }

            $entryPoint = $attributes[0].EntryPoint
            if ([string]::IsNullOrEmpty($entryPoint)) {
                $entryPoint = $method.Name
            }

            [void]$imports.Add($entryPoint)
        }
    }

    return $imports
}

$required = Get-WrapperImportNames -Path $wrapper
$exported = Get-PeExportNames -Path $CandidatePath
$exportedSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($name in $exported) {
    [void]$exportedSet.Add($name)
}

$missing = @($required |
    Where-Object { -not $exportedSet.Contains($_) } |
    Sort-Object)
$blocking = @($missing | Where-Object { $inocuas -notcontains $_ })
$harmless = @($missing | Where-Object { $inocuas -contains $_ })

Write-Host "COMPATIBILIDAD DE PDFIUM CON PdfiumViewer 2.13"
Write-Host "=============================================="
Write-Host ("Candidato        : " + $CandidatePath)
Write-Host ("Tamano           : {0:N1} MiB" -f `
    ((Get-Item -LiteralPath $CandidatePath).Length / 1MB))
Write-Host ("Exporta          : {0} funciones" -f $exported.Count)
Write-Host ("El wrapper exige : {0} funciones" -f $required.Count)
Write-Host ("Faltan           : {0} ({1} bloqueantes)" -f `
    $missing.Count, $blocking.Count)
Write-Host ""

if ($blocking.Count -gt 0) {
    Write-Host "Ausencias que rompen la aplicacion:"
    foreach ($name in $blocking) {
        Write-Host ("  - " + $name)
        if ($impacto.ContainsKey($name)) {
            Write-Host ("      " + $impacto[$name])
        }
        else {
            Write-Host "      Impacto no catalogado: rastrear en el IL del wrapper."
        }
    }

    Write-Host ""
}

if ($harmless.Count -gt 0) {
    Write-Host "Ausencias sin consecuencias (el wrapper no las llama):"
    foreach ($name in $harmless) {
        Write-Host ("  - " + $name)
    }

    Write-Host ""
}

if ($blocking.Count -eq 0) {
    Write-Host "RESULTADO=COMPATIBLE"
    Write-Host ""
    Write-Host "Esto solo comprueba que existen los simbolos, no que se"
    Write-Host "comporten igual. Siete anos de diferencia pueden traer cambios"
    Write-Host "de render o de semantica que no se ven aqui: antes de adoptar el"
    Write-Host "candidato hay que pasar la bateria completa de QA y revisar las"
    Write-Host "capturas de render una a una."
    exit 0
}

Write-Host "RESULTADO=INCOMPATIBLE"
Write-Host ""
Write-Host "Adoptar este pdfium.dll exige bifurcar PdfiumViewer (Apache 2.0) y"
Write-Host "parchear esas llamadas antes de volver a probarlo."
exit 1
