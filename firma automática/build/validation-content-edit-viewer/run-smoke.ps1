param()

$ErrorActionPreference = "Stop"

# PDFLigero.exe targets .NET Framework. PowerShell 7+ runs on modern .NET,
# where BinaryFormatter has been removed and legacy WinForms designer state in
# PdfiumViewer cannot be initialized. Relaunch this validation in the inbox
# Windows PowerShell host so it exercises the same runtime as the application.
if ($PSVersionTable.PSEdition -eq "Core") {
    $windowsPowerShell = Join-Path `
        $env:WINDIR `
        "System32\WindowsPowerShell\v1.0\powershell.exe"
    $legacyCopy = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("PDFLigeroQa-" + [Guid]::NewGuid().ToString("N") + ".ps1")
    $previousQaSource = [Environment]::GetEnvironmentVariable(
        "PDFLIGERO_QA_SOURCE_SCRIPT")
    try {
        [Environment]::SetEnvironmentVariable(
            "PDFLIGERO_QA_SOURCE_SCRIPT",
            $MyInvocation.MyCommand.Path)
        $source = [System.IO.File]::ReadAllText(
            $MyInvocation.MyCommand.Path,
            [System.Text.Encoding]::UTF8)
        [System.IO.File]::WriteAllText(
            $legacyCopy,
            $source,
            (New-Object System.Text.UnicodeEncoding($false, $true)))
        & $windowsPowerShell `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $legacyCopy
        $legacyExitCode = $LASTEXITCODE
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            "PDFLIGERO_QA_SOURCE_SCRIPT",
            $previousQaSource)
        if (Test-Path -LiteralPath $legacyCopy) {
            [System.IO.File]::Delete($legacyCopy)
        }
    }

    exit $legacyExitCode
}

$sourceScriptPath = [Environment]::GetEnvironmentVariable(
    "PDFLIGERO_QA_SOURCE_SCRIPT")
if ([string]::IsNullOrWhiteSpace($sourceScriptPath)) {
    $sourceScriptPath = $MyInvocation.MyCommand.Path
}
$validationDir = Split-Path -Parent $sourceScriptPath
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$outputDir = Join-Path $root "build\output"
$exePath = Join-Path $outputDir "PDFLigero.exe"
$packages = Join-Path $root "packages"
$runDir = Join-Path `
    $validationDir `
    ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
     ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
$recoveryDir = Join-Path $runDir "recovery"
$fixtureA = Join-Path $runDir "edicion-texto-A.pdf"
$fixtureB = Join-Path $runDir "edicion-texto-B.pdf"
$selectionCapture = Join-Path $runDir "01-selector-texto-integrado.png"
$revisionCapture = Join-Path $runDir "02-revision-texto-integrada.png"
$reportPath = Join-Path $runDir "qa-report.txt"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Falta el ejecutable de producción: $exePath"
}

New-Item -ItemType Directory -Force -Path $recoveryDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.AppContext]::SetSwitch(
    "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization",
    $true)

$instanceFlags = `
    [System.Reflection.BindingFlags]::Instance -bor `
    [System.Reflection.BindingFlags]::Public -bor `
    [System.Reflection.BindingFlags]::NonPublic
$staticFlags = `
    [System.Reflection.BindingFlags]::Static -bor `
    [System.Reflection.BindingFlags]::Public -bor `
    [System.Reflection.BindingFlags]::NonPublic

$report = New-Object System.Collections.Generic.List[string]
$form = $null
$formWasClosed = $false
$previousRecoveryRoot = [Environment]::GetEnvironmentVariable(
    "PDFLIGERO_RECOVERY_ROOT")
$previousCurrentDirectory = [Environment]::CurrentDirectory

function Read-Field {
    param(
        [object]$Target,
        [string]$Name
    )

    $field = $Target.GetType().GetField($Name, $instanceFlags)
    if ($null -eq $field) {
        throw "No existe el campo $Name en $($Target.GetType().FullName)."
    }

    return $field.GetValue($Target)
}

function Read-Property {
    param(
        [object]$Target,
        [string]$Name
    )

    $property = $Target.GetType().GetProperty($Name, $instanceFlags)
    if ($null -eq $property) {
        throw "No existe la propiedad $Name en $($Target.GetType().FullName)."
    }

    return $property.GetValue($Target, $null)
}

function Set-Property {
    param(
        [object]$Target,
        [string]$Name,
        [object]$Value
    )

    $property = $Target.GetType().GetProperty($Name, $instanceFlags)
    if ($null -eq $property) {
        throw "No existe la propiedad $Name en $($Target.GetType().FullName)."
    }

    $property.SetValue($Target, $Value, $null)
}

function Find-Method {
    param(
        [object]$Target,
        [string]$Name,
        [int]$ParameterCount
    )

    $method = $Target.GetType().GetMethods($instanceFlags) |
        Where-Object {
            $_.Name -eq $Name -and
            $_.GetParameters().Count -eq $ParameterCount
        } |
        Select-Object -First 1
    if ($null -eq $method) {
        throw "No existe el método $Name/$ParameterCount en " +
            "$($Target.GetType().FullName)."
    }

    return $method
}

function Invoke-Method {
    param(
        [object]$Target,
        [string]$Name,
        [object[]]$Arguments
    )

    $method = Find-Method $Target $Name $Arguments.Count
    return $method.Invoke($Target, $Arguments)
}

function Find-StaticMethod {
    param(
        [Type]$Type,
        [string]$Name,
        [int]$ParameterCount
    )

    $method = $Type.GetMethods($staticFlags) |
        Where-Object {
            $_.Name -eq $Name -and
            $_.GetParameters().Count -eq $ParameterCount
        } |
        Select-Object -First 1
    if ($null -eq $method) {
        throw "No existe el método estático $Name/$ParameterCount en " +
            "$($Type.FullName)."
    }

    return $method
}

function Invoke-StaticMethod {
    param(
        [Type]$Type,
        [string]$Name,
        [object[]]$Arguments
    )

    $method = Find-StaticMethod $Type $Name $Arguments.Count
    return $method.Invoke($null, $Arguments)
}

function Pump-Ui {
    param([int]$Milliseconds)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Milliseconds)
    do {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 10
    } while ([DateTime]::UtcNow -lt $deadline)
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutMilliseconds,
        [string]$FailureMessage
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds(
        $TimeoutMilliseconds)
    do {
        [System.Windows.Forms.Application]::DoEvents()
        if (& $Condition) {
            return
        }

        Start-Sleep -Milliseconds 15
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $FailureMessage
}

function Require-State {
    param(
        [bool]$Condition,
        [string]$FailureMessage
    )

    if (-not $Condition) {
        throw $FailureMessage
    }
}

function Capture-Identity {
    param([string]$Path)

    $item = Get-Item -LiteralPath $Path
    return [PSCustomObject]@{
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        Length = $item.Length
        WriteTicks = $item.LastWriteTimeUtc.Ticks
    }
}

function Require-Identity {
    param(
        [string]$Path,
        [object]$Expected
    )

    $actual = Capture-Identity $Path
    Require-State `
        ($actual.Hash -eq $Expected.Hash -and
         $actual.Length -eq $Expected.Length -and
         $actual.WriteTicks -eq $Expected.WriteTicks) `
        "El visor modificó el PDF original: $Path"
}

function New-FixturePdf {
    param(
        [string]$Path,
        [string]$Label,
        [int]$PageCount
    )

    $page = [iTextSharp.text.PageSize]::A4
    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $document = New-Object iTextSharp.text.Document(
        $page,
        0,
        0,
        0,
        0)
    try {
        $writer = [iTextSharp.text.pdf.PdfWriter]::GetInstance(
            $document,
            $stream)
        $document.AddTitle("Fixture visor edición de texto $Label") |
            Out-Null
        $document.AddAuthor("PDF Ligero QA") | Out-Null
        $document.Open()
        $font = [iTextSharp.text.pdf.BaseFont]::CreateFont(
            [iTextSharp.text.pdf.BaseFont]::HELVETICA,
            [iTextSharp.text.pdf.BaseFont]::CP1252,
            [iTextSharp.text.pdf.BaseFont]::NOT_EMBEDDED)

        for ($index = 0; $index -lt $PageCount; $index++) {
            if ($index -gt 0) {
                $document.NewPage() | Out-Null
            }

            $canvas = $writer.DirectContent
            $canvas.SaveState()
            $canvas.SetColorStroke(
                (New-Object iTextSharp.text.BaseColor(42, 44, 46)))
            $canvas.SetLineWidth(0.8)
            $canvas.Rectangle(36, 36, $page.Width - 72, $page.Height - 72)
            $canvas.Stroke()
            $canvas.BeginText()
            $canvas.SetFontAndSize($font, 22)
            $canvas.SetTextMatrix(72, $page.Height - 92)
            $canvas.ShowText("EDICION DE TEXTO / VISOR QA / $Label")
            $canvas.SetFontAndSize($font, 12)
            $canvas.SetTextMatrix(96, $page.Height - 205)
            $canvas.ShowText("TEXTO ORIGINAL PARA CUBRIR Y REEMPLAZAR")
            $canvas.SetTextMatrix(96, $page.Height - 230)
            $canvas.ShowText(
                "PAGINA " + ($index + 1) + " / ORIGINAL INTACTO")
            $canvas.EndText()
            $canvas.RestoreState()

            if ($index -eq 0) {
                $fieldRectangle = New-Object iTextSharp.text.Rectangle(
                    96,
                    520,
                    360,
                    554)
                $field = New-Object iTextSharp.text.pdf.TextField(
                    $writer,
                    $fieldRectangle,
                    "project.name")
                $field.Text = "VALOR DE FORMULARIO INTACTO"
                $field.FontSize = 10
                $writer.AddAnnotation($field.GetTextField())
            }
        }

        $document.Close()
    }
    finally {
        if ($document.IsOpen()) {
            $document.Close()
        }

        $stream.Dispose()
    }
}

function Get-SelectionPoints {
    param([object]$Renderer)

    $pageIndex = [int](Read-Property $Renderer "Page")
    $document = Read-Property $Renderer "Document"
    $size = $document.PageSizes[$pageIndex]
    $pdfRectangle = New-Object PdfiumViewer.PdfRectangle(
        $pageIndex,
        ([System.Drawing.RectangleF]::new(
            0,
            0,
            [single]$size.Width,
            [single]$size.Height)))
    $pageBounds = $Renderer.BoundsFromPdf($pdfRectangle)
    $visibleBounds = [System.Drawing.Rectangle]::Intersect(
        $pageBounds,
        $Renderer.ClientRectangle)
    Require-State `
        ($visibleBounds.Width -ge 180 -and
         $visibleBounds.Height -ge 130) `
        "La página visible es demasiado pequeña para probar el selector."

    $left = $visibleBounds.Left +
        [Math]::Max(24, [int]($visibleBounds.Width * 0.18))
    $right = $visibleBounds.Right -
        [Math]::Max(24, [int]($visibleBounds.Width * 0.18))
    $top = $visibleBounds.Top +
        [Math]::Max(45, [int]($visibleBounds.Height * 0.20))
    $bottom = [Math]::Min(
        $visibleBounds.Bottom - 28,
        $top + [Math]::Max(90, [int]($visibleBounds.Height * 0.18)))

    return [PSCustomObject]@{
        Start = [System.Drawing.Point]::new($left, $top)
        Finish = [System.Drawing.Point]::new($right, $bottom)
    }
}

function Make-ControllerSelection {
    param(
        [object]$Controller,
        [object]$Points
    )

    $beginArguments = New-Object object[] 1
    $beginArguments[0] = $Points.Start
    Require-State `
        ([bool](Invoke-Method `
            $Controller `
            "BeginSelection" `
            $beginArguments)) `
        "El controlador no inició la selección."

    $updateArguments = New-Object object[] 1
    $updateArguments[0] = $Points.Finish
    Require-State `
        ([bool](Invoke-Method `
            $Controller `
            "UpdateSelection" `
            $updateArguments)) `
        "El controlador no actualizó la selección."

    $completeArguments = New-Object object[] 1
    $completeArguments[0] = $Points.Finish
    Require-State `
        ([bool](Invoke-Method `
            $Controller `
            "CompleteSelection" `
            $completeArguments)) `
        "El controlador no completó la selección."
}

function Send-ViewerShortcut {
    param(
        [object]$ViewerForm,
        [System.Windows.Forms.Keys]$Keys
    )

    $event = [System.Windows.Forms.KeyEventArgs]::new($Keys)
    $arguments = New-Object object[] 2
    $arguments[0] = $ViewerForm
    $arguments[1] = $event
    Invoke-Method `
        $ViewerForm `
        "PdfViewerForm_KeyDown" `
        $arguments |
        Out-Null
    Require-State `
        ($event.Handled -and $event.SuppressKeyPress) `
        "El visor no consumió el atajo $Keys."
    Pump-Ui 100
}

function Assert-DisposedMessageFilter {
    param(
        [object]$Controller,
        [IntPtr]$FormerHandle
    )

    Require-State `
        ([bool](Read-Field $Controller "disposed")) `
        "El controlador cerrado no quedó marcado como disposed."
    $message = [System.Windows.Forms.Message]::Create(
        $FormerHandle,
        0x0100,
        [IntPtr][int][System.Windows.Forms.Keys]::Escape,
        [IntPtr]::Zero)
    $arguments = New-Object object[] 1
    $arguments[0] = $message
    Require-State `
        (-not [bool](Invoke-Method `
            $Controller `
            "PreFilterMessage" `
            $arguments)) `
        "Un filtro de mensajes dispuesto sigue interceptando entradas."
}

function Capture-Window {
    param(
        [System.Windows.Forms.Form]$Window,
        [string]$Path
    )

    $Window.Activate() | Out-Null
    $Window.BringToFront()
    $Window.TopMost = $true
    Pump-Ui 220

    $bitmap = New-Object System.Drawing.Bitmap(
        [Math]::Max(1, $Window.Width),
        [Math]::Max(1, $Window.Height),
        [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    try {
        $Window.DrawToBitmap(
            $bitmap,
            [System.Drawing.Rectangle]::new(
                0,
                0,
                $Window.Width,
                $Window.Height))
        $bitmap.Save(
            $Path,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
        $Window.TopMost = $false
    }

    Require-State `
        ((Get-Item -LiteralPath $Path).Length -gt 12000) `
        "La captura integrada está vacía o incompleta."
}

try {
    Write-Host "STEP 01 entorno"
    [Environment]::SetEnvironmentVariable(
        "PDFLIGERO_RECOVERY_ROOT",
        $recoveryDir)
    [Environment]::CurrentDirectory = $outputDir

    [Reflection.Assembly]::LoadFrom(
        (Join-Path `
            $packages `
            "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll")) |
        Out-Null
    [Reflection.Assembly]::LoadFrom(
        (Join-Path `
            $packages `
            "iTextSharp.5.5.13.3\lib\itextsharp.dll")) |
        Out-Null

    New-FixturePdf $fixtureA "A" 2
    New-FixturePdf $fixtureB "B" 1
    Write-Host "STEP 02 fixtures"
    $identityA = Capture-Identity $fixtureA
    $identityB = Capture-Identity $fixtureB

    [Reflection.Assembly]::LoadFrom(
        (Join-Path $outputDir "PdfiumViewer.dll")) |
        Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($exePath)
    $viewerType = $assembly.GetType(
        "FirmaAutomatica.PdfViewerForm",
        $true)
    $editServiceType = $assembly.GetType(
        "FirmaAutomatica.PdfTextEditService",
        $true)
    $replacementType = $assembly.GetType(
        "FirmaAutomatica.PdfTextReplacement",
        $true)
    $editSessionType = $assembly.GetType(
        "FirmaAutomatica.PdfEditSession",
        $true)
    Write-Host "STEP 03 ensamblado"

    $constructor = $viewerType.GetConstructors($instanceFlags) |
        Where-Object {
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -like "IEnumerable*"
        } |
        Select-Object -First 1
    Require-State `
        ($null -ne $constructor) `
        "No se encontró el constructor del visor."

    [System.Windows.Forms.Application]::EnableVisualStyles()
    $constructorArguments = New-Object object[] 1
    $constructorArguments[0] = [string[]]@($fixtureA, $fixtureB)
    $form = $constructor.Invoke($constructorArguments)
    Write-Host "STEP 04 formulario construido"
    $workingArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = [System.Drawing.Point]::new(
        $workingArea.Left + 10,
        $workingArea.Top + 10)
    $form.Size = [System.Drawing.Size]::new(
        [Math]::Min(1480, $workingArea.Width - 20),
        [Math]::Min(900, $workingArea.Height - 20))
    $form.ShowInTaskbar = $false
    $form.Show()
    Write-Host "STEP 05 formulario mostrado"

    Wait-Until `
        {
            $tabs = Read-Field $form "documentTabs"
            $active = Read-Field $form "activeWorkspace"
            $tabs.TabPages.Count -eq 2 -and
                $null -ne $active -and
                [bool](Read-Field $active "IsLoaded")
        } `
        30000 `
        "El visor no abrió las dos pestañas."
    Write-Host "STEP 06 pestañas cargadas"

    $tabs = Read-Field $form "documentTabs"
    $workspaceA = Read-Field $form "activeWorkspace"
    $workspaceB = $tabs.TabPages[1].Tag
    $viewerA = Read-Field $workspaceA "Viewer"
    $rendererA = Read-Property $viewerA "Renderer"
    $rendererA.Focus() | Out-Null
    Pump-Ui 120

    $contentButton = Read-Field $form "contentEditToolButton"
    $contentMenu = Read-Field $form "contentEditMenu"
    $editTextMenu = Read-Field $form "editTextMenuItem"
    $fillFormMenu = Read-Field $form "fillFormMenuItem"
    $moreEditTextMenu = Read-Field $form "moreEditTextMenuItem"
    $moreFillFormMenu = Read-Field $form "moreFillFormMenuItem"
    Require-State `
        ($contentButton.Enabled -and
         $contentButton.Text -eq "T" -and
         $contentButton.AccessibleName -eq "Texto y formularios") `
        "El botón T no tiene el estado o accesibilidad esperados."
    foreach ($menuItem in @(
        $editTextMenu,
        $moreEditTextMenu
    )) {
        Require-State `
            ($menuItem.Enabled -and
             $menuItem.Text -like "Cubrir y reemplazar texto*") `
            "Falta la entrada de edición de texto en un menú."
    }
    foreach ($menuItem in @(
        $fillFormMenu,
        $moreFillFormMenu
    )) {
        Require-State `
            ($menuItem.Enabled -and
             $menuItem.Text -like "Rellenar formulario PDF*") `
            "Falta la entrada de formularios en un menú."
    }
    $contentButton.PerformClick()
    Pump-Ui 100
    Require-State $contentMenu.Visible "El botón T no abrió su menú."
    $contentMenu.Close()
    Pump-Ui 60
    Require-State `
        ($null -eq (Read-Field $workspaceA "TextEditSelection") -and
         $null -eq (Read-Field $workspaceB "TextEditSelection")) `
        "El selector dejó de ser lazy antes de utilizarlo."
    $report.Add(
        "PASS - botón T, menús de texto/formularios y creación lazy")
    Write-Host "STEP 07 menús"

    $pointsA = Get-SelectionPoints $rendererA
    $zoomControllerA = Read-Field $workspaceA "RectangleZoom"
    Make-ControllerSelection $zoomControllerA $pointsA
    Require-State `
        ([bool](Read-Property $zoomControllerA "HasSelection")) `
        "No se preparó la selección previa de zoom."
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    $controllerA = Read-Field $workspaceA "TextEditSelection"
    Require-State `
        ($null -ne $controllerA -and
         [bool](Read-Property $controllerA "IsActive") -and
         -not [bool](Read-Property $zoomControllerA "HasSelection")) `
        "Ctrl+E no activó texto o no canceló el rectángulo de zoom."
    $report.Add(
        "PASS - Ctrl+E crea el selector y cancela el rectángulo de zoom")
    Write-Host "STEP 08 activación"

    Send-ViewerShortcut $form ([System.Windows.Forms.Keys]::Escape)
    Invoke-Method $form "ShowSearchPanel" ([object[]]@()) | Out-Null
    $searchPanel = Read-Field $form "searchPanel"
    Require-State $searchPanel.Visible "No se pudo abrir la búsqueda."
    $rendererA.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    Require-State `
        (-not $searchPanel.Visible -and
         [bool](Read-Property $controllerA "IsActive")) `
        "Activar texto no cerró la búsqueda."

    Send-ViewerShortcut $form ([System.Windows.Forms.Keys]::Escape)
    $measureButton = Read-Field $form "measureToolButton"
    $measureButton.PerformClick()
    Pump-Ui 100
    $measurementA = Read-Field $workspaceA "Measurement"
    Require-State `
        ($null -ne $measurementA -and
         [bool](Read-Property $measurementA "IsActive")) `
        "No se pudo activar la medición para comprobar exclusión."
    $rendererA.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    Require-State `
        ([bool](Read-Property $measurementA "IsActive") -and
         -not [bool](Read-Property $controllerA "IsActive")) `
        "Texto y medición quedaron activos a la vez."
    $measureButton.PerformClick()
    Pump-Ui 80
    $rendererA.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))

    $searchButton = Read-Field $form "searchToolButton"
    $zoomInMenu = Read-Field $form "zoomInMenuItem"
    Require-State `
        (-not $searchButton.Enabled -and
         -not $measureButton.Enabled -and
         [bool]$zoomInMenu.Enabled) `
        "La exclusión de herramientas no conserva solo el zoom visual."
    $canZoomArguments = New-Object object[] 1
    $canZoomArguments[0] = $workspaceA
    Require-State `
        (-not [bool](Invoke-Method `
            $form `
            "CanUseRectangleZoom" `
            $canZoomArguments)) `
        "El gesto de zoom rectangular sigue disponible durante texto."
    $zoomBeginArguments = New-Object object[] 1
    $zoomBeginArguments[0] = $pointsA.Start
    Require-State `
        (-not [bool](Invoke-Method `
            $zoomControllerA `
            "BeginSelection" `
            $zoomBeginArguments)) `
        "Zoom rectangular y texto pudieron iniciar gestos simultáneos."
    $report.Add(
        "PASS - exclusión mutua con búsqueda, medición y gesto de zoom")
    Write-Host "STEP 09 exclusión"

    [System.Windows.Forms.Cursor]::Position =
        $rendererA.PointToScreen($pointsA.Start)
    Invoke-Method $controllerA "Activate" ([object[]]@()) | Out-Null
    Require-State `
        ([bool](Read-Property $controllerA "IsPrecisionCursorApplied")) `
        "El selector no aplicó el cursor de cruz preciso."
    $markerCountBefore = $rendererA.Markers.Count
    Make-ControllerSelection $controllerA $pointsA
    $selection = Read-Property $controllerA "Selection"
    $acceptButtonA = Read-Property $controllerA "AcceptButton"
    Require-State `
        ([bool](Read-Property $controllerA "HasSelection") -and
         $acceptButtonA.Visible -and
         $acceptButtonA.Text -eq "T" -and
         $rendererA.Markers.Count -eq ($markerCountBefore + 1)) `
        "La selección no muestra marco y confirmación T superpuestos."
    Capture-Window $form $selectionCapture
    $report.Add(
        "PASS - cruz precisa, rectángulo, marcador y T central integrados")
    Write-Host "STEP 10 selector capturado"

    $tabs.SelectedIndex = 1
    Wait-Until `
        {
            $active = Read-Field $form "activeWorkspace"
            $active -eq $workspaceB -and
                [bool](Read-Field $workspaceB "IsLoaded")
        } `
        30000 `
        "La segunda pestaña no se activó."
    Require-State `
        (-not [bool](Read-Property $controllerA "IsActive") -and
         -not [bool](Read-Property $controllerA "HasSelection") -and
         $rendererA.Markers.Count -eq $markerCountBefore -and
         $null -eq (Read-Field $workspaceB "TextEditSelection")) `
        "Cambiar de pestaña no desactivó limpiamente el selector."

    $viewerB = Read-Field $workspaceB "Viewer"
    $rendererB = Read-Property $viewerB "Renderer"
    $rendererB.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    $controllerB = Read-Field $workspaceB "TextEditSelection"
    $acceptButtonB = Read-Property $controllerB "AcceptButton"
    $rendererBHandle = $rendererB.Handle
    Require-State `
        ($null -ne $controllerB -and
         [bool](Read-Property $controllerB "IsActive")) `
        "La segunda pestaña no creó su selector independiente."
    Require-State `
        ([bool]$tabs.CloseActiveTab()) `
        "La segunda pestaña rechazó el cierre limpio."
    Wait-Until `
        {
            $tabs.TabPages.Count -eq 1 -and
                [bool](Read-Field $workspaceB "IsDisposed")
        } `
        5000 `
        "La segunda pestaña no terminó de cerrarse."
    Require-State `
        ($null -eq (Read-Field $workspaceB "TextEditSelection") -and
         $null -eq (Read-Field $workspaceB "RectangleZoom") -and
         $acceptButtonB.IsDisposed) `
        "Cerrar pestaña dejó controles o controladores residuales."
    Assert-DisposedMessageFilter $controllerB $rendererBHandle
    $report.Add(
        "PASS - cambio/cierre de pestaña elimina control, marcador y filtro")
    Write-Host "STEP 11 pestaña cerrada"

    Wait-Until `
        {
            (Read-Field $form "activeWorkspace") -eq $workspaceA
        } `
        5000 `
        "No se restauró la primera pestaña."
    $rendererA.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    Require-State `
        ((Read-Field $workspaceA "TextEditSelection") -eq $controllerA -and
         [bool](Read-Property $controllerA "IsActive")) `
        "La pestaña conservada no reutilizó su selector de forma segura."
    Make-ControllerSelection $controllerA $pointsA
    $selection = Read-Property $controllerA "Selection"
    Invoke-Method $controllerA "Deactivate" ([object[]]@()) | Out-Null
    Require-State `
        ($rendererA.Markers.Count -eq $markerCountBefore -and
         -not $acceptButtonA.Visible) `
        "Desactivar el selector dejó un marcador visible."

    $sessionA = Read-Field $workspaceA "EditSession"
    $sourcePath = [string](Read-Field $workspaceA "ContentPath")
    $reserveArguments = New-Object object[] 1
    $reserveArguments[0] = [long](2 * 1024 * 1024)
    $revisionPath = [string](Invoke-Method `
        $sessionA `
        "ReserveRevisionPath" `
        $reserveArguments)

    $analyzeArguments = New-Object object[] 1
    $analyzeArguments[0] = $sourcePath
    $analysis = Invoke-StaticMethod `
        $editServiceType `
        "Analyze" `
        $analyzeArguments
    $regionArguments = New-Object object[] 3
    $regionArguments[0] = $analysis
    $regionArguments[1] = [int]$selection.Page
    $regionArguments[2] = $selection.Bounds
    $region = Invoke-StaticMethod `
        $editServiceType `
        "CreateRegionFromPdfBounds" `
        $regionArguments

    $replacementConstructor = $replacementType.GetConstructors(
        $instanceFlags) |
        Where-Object { $_.GetParameters().Count -eq 2 } |
        Select-Object -First 1
    Require-State `
        ($null -ne $replacementConstructor) `
        "No se encontró el constructor del reemplazo de texto."
    $replacementArguments = New-Object object[] 2
    $replacementArguments[0] = $region
    $replacementArguments[1] =
        "REVISIÓN VISOR QA · Málaga · año · Ω"
    $replacement = $replacementConstructor.Invoke($replacementArguments)
    Set-Property $replacement "FontSizePoints" ([single]18)
    Set-Property $replacement "MinimumFontSizePoints" ([single]5)
    Set-Property $replacement "AutoFit" $true
    Set-Property $replacement "CoverOriginal" $true
    Set-Property $replacement "TextColor" ([System.Drawing.Color]::Black)
    Set-Property $replacement "CoverColor" ([System.Drawing.Color]::White)

    $saveArguments = New-Object object[] 4
    $saveArguments[0] = $sourcePath
    $saveArguments[1] = $revisionPath
    $saveArguments[2] = $analysis
    $saveArguments[3] = $replacement
    $saveResult = Invoke-StaticMethod `
        $editServiceType `
        "Save" `
        $saveArguments
    Write-Host "STEP 12 revisión escrita"
    Require-State `
        ((Test-Path -LiteralPath $revisionPath) -and
         [string](Read-Property $saveResult "OutputPath") -eq
            [System.IO.Path]::GetFullPath($revisionPath)) `
        "El motor no creó la revisión reservada por Recovery."

    $applyArguments = New-Object object[] 7
    $applyArguments[0] = $workspaceA
    $applyArguments[1] = $sessionA
    $applyArguments[2] = $sourcePath
    $applyArguments[3] = $revisionPath
    $applyArguments[4] = [int]$selection.Page
    $applyArguments[5] = "Texto editado por QA integrado"
    $applyArguments[6] = "texto QA actualizado"
    Invoke-Method `
        $form `
        "ApplyContentRevision" `
        $applyArguments |
        Out-Null
    Pump-Ui 220

    $sessionDirectory = [string](Read-Property $sessionA "SessionDirectory")
    $manifestPath = Join-Path $sessionDirectory "recovery.txt"
    Require-State `
        ([string](Read-Field $workspaceA "ContentPath") -eq
            [System.IO.Path]::GetFullPath($revisionPath) -and
         [bool](Read-Property $sessionA "HasUnsavedChanges") -and
         [bool](Read-Property $sessionA "CanUndo") -and
         (Test-Path -LiteralPath $manifestPath)) `
        "ApplyContentRevision no activó historial y Recovery."
    Require-State `
        ($null -eq (Read-Field $workspaceA "TextEditSelection") -and
         $null -eq (Read-Field $workspaceA "Measurement") -and
         $acceptButtonA.IsDisposed) `
        "La recarga dejó selectores o medición enlazados a la revisión anterior."
    Assert-DisposedMessageFilter $controllerA $rendererA.Handle
    $measurementToolbar = Read-Property $measurementA "Toolbar"
    Require-State `
        ([bool](Read-Field $measurementA "disposed") -and
         $measurementToolbar.IsDisposed) `
        "La recarga no liberó el controlador de medición inactivo."

    $candidateArguments = [object[]]@()
    $recoveryCandidates = Invoke-StaticMethod `
        $editSessionType `
        "FindRecoverableSessions" `
        $candidateArguments
    Require-State `
        ($recoveryCandidates.Count -eq 1 -and
         [string](Read-Property $recoveryCandidates[0] "CurrentPath") -eq
            [System.IO.Path]::GetFullPath($revisionPath)) `
        "Recovery no descubre la revisión activa."

    $revisedAnalysisArguments = New-Object object[] 1
    $revisedAnalysisArguments[0] = $revisionPath
    $revisedAnalysis = Invoke-StaticMethod `
        $editServiceType `
        "Analyze" `
        $revisedAnalysisArguments
    $extractArguments = New-Object object[] 2
    $extractArguments[0] = $revisedAnalysis
    $extractArguments[1] = $region
    $extracted = [string](Invoke-StaticMethod `
        $editServiceType `
        "ExtractText" `
        $extractArguments)
    Require-State `
        ($extracted -like "*REVISIÓN VISOR QA*" -and
         $extracted -like "*Málaga*" -and
         $extracted -like "*Ω*") `
        "La revisión activa no conserva texto Unicode buscable."
    Capture-Window $form $revisionCapture
    $report.Add(
        "PASS - motor + ApplyContentRevision recargan PDF y registran Recovery")
    Write-Host "STEP 13 revisión aplicada"

    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::Z))
    Wait-Until `
        {
            [string](Read-Field $workspaceA "ContentPath") -eq $sourcePath
        } `
        10000 `
        "Ctrl+Z no volvió al PDF original."
    Require-State `
        (-not [bool](Read-Property $sessionA "HasUnsavedChanges") -and
         [bool](Read-Property $sessionA "CanRedo") -and
         -not (Test-Path -LiteralPath $manifestPath)) `
        "Undo no sincronizó el estado limpio de Recovery."

    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::Y))
    Wait-Until `
        {
            [string](Read-Field $workspaceA "ContentPath") -eq
                [System.IO.Path]::GetFullPath($revisionPath)
        } `
        10000 `
        "Ctrl+Y no restauró la revisión."
    Require-State `
        ([bool](Read-Property $sessionA "HasUnsavedChanges") -and
         (Test-Path -LiteralPath $manifestPath)) `
        "Redo no restauró Recovery."

    $rendererA = Read-Property (Read-Field $workspaceA "Viewer") "Renderer"
    $rendererA.Focus() | Out-Null
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::E))
    $controllerAfterReload = Read-Field $workspaceA "TextEditSelection"
    $afterReloadButton = Read-Property $controllerAfterReload "AcceptButton"
    Require-State `
        ($controllerAfterReload -ne $controllerA -and
         [bool](Read-Property $controllerAfterReload "IsActive")) `
        "Tras recargar no se creó un selector nuevo y limpio."

    Send-ViewerShortcut $form ([System.Windows.Forms.Keys]::Escape)
    Send-ViewerShortcut `
        $form `
        ([System.Windows.Forms.Keys](
            [System.Windows.Forms.Keys]::Control -bor
            [System.Windows.Forms.Keys]::Z))
    Wait-Until `
        {
            [string](Read-Field $workspaceA "ContentPath") -eq $sourcePath
        } `
        10000 `
        "El Undo final no volvió al original."
    Require-State `
        ($null -eq (Read-Field $workspaceA "TextEditSelection") -and
         $afterReloadButton.IsDisposed -and
         [bool](Read-Field $controllerAfterReload "disposed")) `
        "Undo/recarga dejó el segundo selector conectado."
    Assert-DisposedMessageFilter $controllerAfterReload $rendererA.Handle
    $report.Add(
        "PASS - Ctrl+Z/Ctrl+Y, recarga y recreación lazy sin filtros residuales")
    Write-Host "STEP 14 undo redo"

    Require-Identity $fixtureA $identityA
    Require-Identity $fixtureB $identityB
    $report.Add(
        "PASS - SHA-256, longitud y fecha de ambos originales intactos")

    $form.Close()
    Write-Host "STEP 15 close retornó"
    Pump-Ui 220
    $formWasClosed = $true
    Require-State `
        ([bool](Read-Field $workspaceA "IsDisposed") -and
         $null -eq (Read-Field $workspaceA "TextEditSelection") -and
         $null -eq (Read-Field $workspaceA "Measurement") -and
         $null -eq (Read-Field $workspaceA "RectangleZoom") -and
         -not (Test-Path -LiteralPath $sessionDirectory)) `
        "Cerrar el visor dejó controladores o Recovery limpio residuales."
    $report.Add(
        "PASS - cierre del visor libera controladores y elimina Recovery limpio")

    $temporaryFiles = Get-ChildItem `
        -LiteralPath $runDir `
        -Recurse `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ".*.tmp" -or $_.Extension -eq ".tmp" }
    Require-State `
        (@($temporaryFiles).Count -eq 0) `
        "Quedaron archivos temporales tras el recorrido."
    Require-Identity $fixtureA $identityA
    Require-Identity $fixtureB $identityB

    $report.Add(
        "EXE_SHA256=" +
        (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash)
    $report.Add("CAPTURA_SELECTOR=$selectionCapture")
    $report.Add("CAPTURA_REVISION=$revisionCapture")
    $report.Add("RESULTADO GLOBAL: PASS")
}
catch {
    $report.Add("RESULTADO GLOBAL: FAIL")
    $report.Add($_.Exception.ToString())
    throw
}
finally {
    if ($null -ne $form -and -not $formWasClosed) {
        try {
            $form.Dispose()
            Pump-Ui 80
        }
        catch {
        }
    }

    [Environment]::SetEnvironmentVariable(
        "PDFLIGERO_RECOVERY_ROOT",
        $previousRecoveryRoot)
    [Environment]::CurrentDirectory = $previousCurrentDirectory
    $report | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Set-Content `
        -LiteralPath (Join-Path $validationDir "latest-run.txt") `
        -Value $runDir `
        -Encoding UTF8
}

$report | ForEach-Object { Write-Host $_ }
Write-Host "REPORT=$reportPath"
