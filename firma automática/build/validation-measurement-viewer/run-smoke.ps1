param()

$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$outputDir = Join-Path $root "build\output"
$exePath = Join-Path $outputDir "PDFLigero.exe"
$packages = Join-Path $root "packages"
$runDir = Join-Path `
    $validationDir `
    ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
     ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
$recoveryDir = Join-Path $runDir "recovery"
$fixtureA = Join-Path $runDir "plano-medicion-A.pdf"
$fixtureB = Join-Path $runDir "plano-medicion-B.pdf"
$screenshotPath = Join-Path $runDir "01-medicion-integrada.png"
$reportPath = Join-Path $runDir "qa-report.txt"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Falta el ejecutable de producción: $exePath"
}

New-Item -ItemType Directory -Force -Path $recoveryDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$instanceFlags = `
    [System.Reflection.BindingFlags]::Instance -bor `
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
        "El visor modificó el fixture original: $Path"
}

function Draw-PlanPage {
    param(
        [object]$Canvas,
        [object]$Page,
        [int]$PageNumber,
        [string]$Revision
    )

    $dark = New-Object iTextSharp.text.BaseColor(42, 44, 46)
    $muted = New-Object iTextSharp.text.BaseColor(184, 181, 174)
    $coral = New-Object iTextSharp.text.BaseColor(238, 91, 61)

    $Canvas.SaveState()
    $Canvas.SetColorStroke($dark)
    $Canvas.SetLineWidth(0.85)
    $Canvas.Rectangle(
        28,
        28,
        $Page.Width - 56,
        $Page.Height - 56)
    $Canvas.Stroke()

    $Canvas.SetColorStroke($muted)
    $Canvas.SetLineWidth(0.25)
    for ($x = 72; $x -lt $Page.Width - 60; $x += 36) {
        $Canvas.MoveTo($x, 72)
        $Canvas.LineTo($x, $Page.Height - 72)
    }
    for ($y = 72; $y -lt $Page.Height - 60; $y += 36) {
        $Canvas.MoveTo(72, $y)
        $Canvas.LineTo($Page.Width - 72, $y)
    }
    $Canvas.Stroke()

    $Canvas.SetColorStroke($dark)
    $Canvas.SetLineWidth(3.1)
    $buildingWidth = [Math]::Min(
        $Page.Width - 210,
        [Math]::Max(260, $Page.Width * 0.62))
    $buildingHeight = [Math]::Min(
        $Page.Height - 230,
        [Math]::Max(260, $Page.Height * 0.48))
    $Canvas.Rectangle(105, 120, $buildingWidth, $buildingHeight)
    $Canvas.MoveTo(105 + $buildingWidth * 0.46, 120)
    $Canvas.LineTo(
        105 + $buildingWidth * 0.46,
        120 + $buildingHeight)
    $Canvas.MoveTo(105, 120 + $buildingHeight * 0.54)
    $Canvas.LineTo(
        105 + $buildingWidth,
        120 + $buildingHeight * 0.54)
    $Canvas.Stroke()

    $Canvas.SetColorStroke($coral)
    $Canvas.SetLineWidth(0.8)
    $Canvas.MoveTo(105, 96)
    $Canvas.LineTo(105 + $buildingWidth, 96)
    $Canvas.MoveTo(105, 89)
    $Canvas.LineTo(105, 103)
    $Canvas.MoveTo(105 + $buildingWidth, 89)
    $Canvas.LineTo(105 + $buildingWidth, 103)
    $Canvas.Stroke()

    $font = [iTextSharp.text.pdf.BaseFont]::CreateFont(
        [iTextSharp.text.pdf.BaseFont]::HELVETICA,
        [iTextSharp.text.pdf.BaseFont]::CP1252,
        [iTextSharp.text.pdf.BaseFont]::NOT_EMBEDDED)
    $Canvas.BeginText()
    $Canvas.SetColorFill($dark)
    $Canvas.SetFontAndSize($font, 13)
    $Canvas.SetTextMatrix(52, $Page.Height - 54)
    $Canvas.ShowText(
        "PLANO QA / REVISION $Revision / HOJA $PageNumber")
    $Canvas.SetColorFill($coral)
    $Canvas.SetFontAndSize($font, 8)
    $Canvas.SetTextMatrix(105, 76)
    $Canvas.ShowText("COTA DE CONTROL 5,00 m")
    $Canvas.SetColorFill($dark)
    $Canvas.SetFontAndSize($font, 7)
    for ($index = 0; $index -lt 10; $index++) {
        $Canvas.SetTextMatrix(
            82 + $index * 46,
            112 + ($index % 2) * 13)
        $Canvas.ShowText("EJE " + ($index + 1))
    }
    $Canvas.EndText()
    $Canvas.RestoreState()
}

function New-FixturePdf {
    param(
        [string]$Path,
        [string]$Revision
    )

    $pageSizes = @(
        [iTextSharp.text.PageSize]::A3.Rotate(),
        [iTextSharp.text.PageSize]::A4,
        [iTextSharp.text.PageSize]::A2.Rotate()
    )
    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $document = New-Object iTextSharp.text.Document(
        $pageSizes[0],
        0,
        0,
        0,
        0)
    try {
        $writer = [iTextSharp.text.pdf.PdfWriter]::GetInstance(
            $document,
            $stream)
        $writer.SetFullCompression()
        $document.AddTitle("Fixture de medición $Revision") | Out-Null
        $document.AddCreator("PDF Ligero QA") | Out-Null
        $document.Open()

        for ($index = 0; $index -lt $pageSizes.Count; $index++) {
            if ($index -gt 0) {
                $document.SetPageSize($pageSizes[$index]) | Out-Null
                $document.NewPage() | Out-Null
            }

            Draw-PlanPage `
                $writer.DirectContent `
                $pageSizes[$index] `
                ($index + 1) `
                $Revision
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

function Invoke-ControllerKeyFilter {
    param(
        [object]$Controller,
        [System.Windows.Forms.Control]$Control,
        [System.Windows.Forms.Keys]$Key
    )

    if (-not $Control.IsHandleCreated) {
        $null = $Control.Handle
    }

    $message = [System.Windows.Forms.Message]::Create(
        $Control.Handle,
        0x0100,
        [IntPtr][int]$Key,
        [IntPtr]::Zero)
    $arguments = New-Object object[] 1
    $arguments[0] = $message
    return [bool](Invoke-Method `
        $Controller `
        "PreFilterMessage" `
        $arguments)
}

function Select-Scale {
    param(
        [object]$Controller,
        [double]$Denominator
    )

    $arguments = New-Object object[] 1
    $arguments[0] = $Denominator
    Invoke-Method $Controller "SelectScale" $arguments | Out-Null
}

function Add-MeasurementPoint {
    param(
        [object]$Controller,
        [int]$PageIndex,
        [single]$X,
        [single]$Y
    )

    $arguments = New-Object object[] 2
    $arguments[0] = $PageIndex
    $arguments[1] = [System.Drawing.PointF]::new($X, $Y)
    return [bool](Invoke-Method `
        $Controller `
        "AddPointForTesting" `
        $arguments)
}

function Capture-Window {
    param(
        [System.Windows.Forms.Form]$Window,
        [string]$Path
    )

    $Window.Activate() | Out-Null
    $Window.BringToFront()
    $Window.TopMost = $true
    Pump-Ui 250

    $bitmap = New-Object System.Drawing.Bitmap(
        [Math]::Max(1, $Window.Width),
        [Math]::Max(1, $Window.Height),
        [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    try {
        # DrawToBitmap captures this exact form tree and cannot include any
        # unrelated desktop window that happens to overlap it.
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

    $image = Get-Item -LiteralPath $Path
    Require-State `
        ($image.Length -gt 30000) `
        "La captura de la ventana está vacía o incompleta."
}

try {
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

    New-FixturePdf $fixtureA "A"
    New-FixturePdf $fixtureB "B"
    $identityA = Capture-Identity $fixtureA
    $identityB = Capture-Identity $fixtureB

    [Reflection.Assembly]::LoadFrom(
        (Join-Path $outputDir "PdfiumViewer.dll")) |
        Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($exePath)
    $viewerType = $assembly.GetType(
        "FirmaAutomatica.PdfViewerForm",
        $true)
    $constructor = $viewerType.GetConstructors($instanceFlags) |
        Where-Object {
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -like "IEnumerable*"
        } |
        Select-Object -First 1
    Require-State `
        ($null -ne $constructor) `
        "No se encontró el constructor del visor."

    $constructorArguments = New-Object object[] 1
    $constructorArguments[0] = [string[]]@($fixtureA, $fixtureB)
    $form = $constructor.Invoke($constructorArguments)
    $workingArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point(
        ($workingArea.Left + 12),
        ($workingArea.Top + 12))
    $form.Size = New-Object System.Drawing.Size(
        ([Math]::Min(1480, $workingArea.Width - 24)),
        ([Math]::Min(900, $workingArea.Height - 24)))
    $form.ShowInTaskbar = $false
    $form.Show()

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

    $tabs = Read-Field $form "documentTabs"
    $workspaceA = Read-Field $form "activeWorkspace"
    $viewerA = Read-Field $workspaceA "Viewer"
    $rendererA = Read-Property $viewerA "Renderer"
    $measureButton = Read-Field $form "measureToolButton"
    $documentLabel = Read-Field $form "documentLabel"
    $currentPageTextBox = Read-Field $form "currentPageTextBox"
    $thumbnailsA = Read-Field $workspaceA "Thumbnails"

    Require-State `
        ($null -eq (Read-Field $workspaceA "Measurement")) `
        "La medición dejó de ser lazy: existe antes de usarla."
    Require-State `
        ([bool]$measureButton.Enabled) `
        "El botón de medición no está disponible con un PDF cargado."
    $report.Add(
        "PASS - apertura normal lazy: Measurement es null en las dos pestañas")

    $viewportBefore = $rendererA.ClientSize
    $measureButton.PerformClick()
    Pump-Ui 160
    $controllerA = Read-Field $workspaceA "Measurement"
    Require-State `
        ($null -ne $controllerA -and
         [bool](Read-Property $controllerA "IsActive")) `
        "El botón no activó el controlador lazy."
    $toolbarA = Read-Property $controllerA "Toolbar"
    Require-State `
        ($toolbarA.Visible -and
         $toolbarA.Parent -eq $rendererA -and
         $rendererA.ClientSize -eq $viewportBefore) `
        "La barra no está superpuesta dentro del renderer."
    Require-State `
        ($documentLabel.Text -eq
         [string](Read-Property $controllerA "StatusText") -and
         $documentLabel.Text -like "*escala*") `
        "El encabezado no muestra el estado inicial sin escala."
    $report.Add(
        "PASS - botón crea y activa una barra flotante sin restar viewport")

    foreach ($fieldName in @(
        "searchToolButton",
        "ocrToolButton",
        "signToolButton",
        "mergeToolButton",
        "compareToolButton",
        "undoMenuItem",
        "redoMenuItem",
        "saveCopyMenuItem",
        "printMenuItem",
        "ocrMenuItem",
        "organizePagesMenuItem",
        "editBookmarksMenuItem"
    )) {
        $control = Read-Field $form $fieldName
        Require-State `
            (-not [bool]$control.Enabled) `
            "La acción $fieldName sigue habilitada durante medición."
    }
    foreach ($fieldName in @(
        "fitWidthMenuItem",
        "zoomInMenuItem",
        "zoomOutMenuItem",
        "rotateLeftMenuItem",
        "rotateRightMenuItem"
    )) {
        $control = Read-Field $form $fieldName
        Require-State `
            ([bool]$control.Enabled) `
            "La navegación visual $fieldName quedó bloqueada."
    }
    Require-State `
        ([bool]$measureButton.Enabled -and
         $measureButton.Text -eq [string][char]0x00D7 -and
         [bool]$currentPageTextBox.Enabled -and
         [bool](Read-Property $thumbnailsA "PageOperationsEnabled") -eq
            $false) `
        "Los estados de herramientas no distinguen medición de navegación."
    $report.Add(
        "PASS - mutaciones bloqueadas; zoom, giro y navegación siguen disponibles")

    Select-Scale $controllerA 100
    Require-State `
        ($documentLabel.Text -eq
         [string](Read-Property $controllerA "StatusText") -and
         $documentLabel.Text -like "Distancia:*") `
        "StatusChanged no actualizó el encabezado tras elegir escala."
    Require-State `
        (Add-MeasurementPoint $controllerA 0 190 250) `
        "No se pudo marcar el primer punto."
    Require-State `
        ($documentLabel.Text -eq "Marca el segundo punto.") `
        "StatusChanged no muestra el siguiente paso en el encabezado."
    Require-State `
        (Add-MeasurementPoint $controllerA 0 550 250) `
        "No se pudo completar la distancia."
    Pump-Ui 220
    Require-State `
        ($documentLabel.Text -eq
         [string](Read-Property $controllerA "StatusText") -and
         $documentLabel.Text -like "*Distancia:*") `
        "El resultado de distancia no llegó al encabezado."
    Capture-Window $form $screenshotPath
    $report.Add(
        "PASS - StatusChanged integrado y captura real con medición visible")

    Require-State `
        (Add-MeasurementPoint $controllerA 0 230 330) `
        "No se pudo iniciar el trazado previo a navegar."
    $currentPageTextBox.Text = "2"
    $currentPageTextBox.Focus() | Out-Null
    Pump-Ui 40
    Require-State `
        (-not (Invoke-ControllerKeyFilter `
            $controllerA `
            $currentPageTextBox `
            ([System.Windows.Forms.Keys]::Enter))) `
        "El filtro de medición secuestra Enter del campo de página."
    $pageKey = [System.Windows.Forms.KeyEventArgs]::new(
        [System.Windows.Forms.Keys]::Enter)
    $pageArguments = New-Object object[] 2
    $pageArguments[0] = $currentPageTextBox
    $pageArguments[1] = $pageKey
    Invoke-Method `
        $form `
        "CurrentPageTextBox_KeyDown" `
        $pageArguments |
        Out-Null
    Wait-Until `
        {
            $rendererA.Page -eq 1 -and
            [int](Read-Property $controllerA "ActivePageIndex") -eq 1
        } `
        5000 `
        "Enter en el campo no navegó a la página 2."
    Require-State `
        ([int](Read-Property $controllerA "DraftPointCount") -eq 0 -and
         [bool](Read-Property $controllerA "IsActive")) `
        "Cambiar de página no canceló solo el trazado incompleto."
    $report.Add(
        "PASS - campo de página conserva Enter y cancela solo el borrador")

    Set-Property $thumbnailsA "SelectedPage" 2
    $thumbnailsA.Focus() | Out-Null
    Pump-Ui 40
    Require-State `
        (-not (Invoke-ControllerKeyFilter `
            $controllerA `
            $thumbnailsA `
            ([System.Windows.Forms.Keys]::Enter))) `
        "El filtro de medición secuestra Enter de las miniaturas."
    $thumbnailMessage = [System.Windows.Forms.Message]::Create(
        $thumbnailsA.Handle,
        0x0100,
        [IntPtr][int][System.Windows.Forms.Keys]::Enter,
        [IntPtr]::Zero)
    $thumbnailArguments = New-Object object[] 2
    $thumbnailArguments[0] = $thumbnailMessage
    $thumbnailArguments[1] = [System.Windows.Forms.Keys]::Enter
    $thumbnailHandled = [bool](Invoke-Method `
        $thumbnailsA `
        "ProcessCmdKey" `
        $thumbnailArguments)
    Wait-Until `
        {
            $rendererA.Page -eq 2 -and
            $currentPageTextBox.Text -eq "3"
        } `
        5000 `
        "Enter en miniaturas no navegó a la página 3."
    Require-State `
        ($thumbnailHandled -and
         [bool](Read-Property $controllerA "IsActive")) `
        "Las miniaturas no conservaron su navegación durante medición."
    $report.Add(
        "PASS - miniaturas conservan Enter y la medición sigue activa")

    $tabs.SelectedIndex = 1
    Wait-Until `
        {
            $active = Read-Field $form "activeWorkspace"
            $active -ne $workspaceA -and
                [bool](Read-Field $active "IsLoaded")
        } `
        30000 `
        "La segunda pestaña no se activó."
    $workspaceB = Read-Field $form "activeWorkspace"
    Require-State `
        (-not [bool](Read-Property $controllerA "IsActive") -and
         -not $toolbarA.Visible -and
         $null -eq (Read-Field $workspaceB "Measurement")) `
        "Cambiar de pestaña no desactivó la sesión anterior."

    $shortcut = [System.Windows.Forms.KeyEventArgs]::new(
        ([System.Windows.Forms.Keys]::Control -bor
         [System.Windows.Forms.Keys]::Shift -bor
         [System.Windows.Forms.Keys]::M))
    $shortcutArguments = New-Object object[] 2
    $shortcutArguments[0] = $form
    $shortcutArguments[1] = $shortcut
    Invoke-Method `
        $form `
        "PdfViewerForm_KeyDown" `
        $shortcutArguments |
        Out-Null
    Pump-Ui 140
    $controllerB = Read-Field $workspaceB "Measurement"
    Require-State `
        ($shortcut.Handled -and
         $shortcut.SuppressKeyPress -and
         $null -ne $controllerB -and
         [bool](Read-Property $controllerB "IsActive")) `
        "Ctrl+Mayús+M no creó la medición en la segunda pestaña."
    $toolbarB = Read-Property $controllerB "Toolbar"
    $report.Add(
        "PASS - Ctrl+Mayús+M activa una sesión lazy independiente por pestaña")

    Require-State `
        ([bool]$tabs.CloseActiveTab()) `
        "La pestaña activa rechazó el cierre."
    Wait-Until `
        {
            $tabs.TabPages.Count -eq 1 -and
            [bool](Read-Field $workspaceB "IsDisposed")
        } `
        5000 `
        "La segunda pestaña no terminó de cerrarse."
    Require-State `
        ($null -eq (Read-Field $workspaceB "Measurement") -and
         $toolbarB.IsDisposed) `
        "Cerrar pestaña no dispuso su controlador y barra."
    $report.Add(
        "PASS - cerrar pestaña dispone filtro, barra, marcadores y controlador")

    $activeAfterClose = Read-Field $form "activeWorkspace"
    Require-State `
        ($activeAfterClose -eq $workspaceA -and
         (Read-Field $workspaceA "Measurement") -eq $controllerA -and
         -not [bool](Read-Property $controllerA "IsActive")) `
        "La sesión de la pestaña conservada cambió de forma inesperada."
    $measureButton.PerformClick()
    Pump-Ui 120
    Require-State `
        ((Read-Field $workspaceA "Measurement") -eq $controllerA -and
         [bool](Read-Property $controllerA "IsActive")) `
        "No se reutilizó de forma segura la sesión al volver a la pestaña."

    $form.Close()
    Pump-Ui 180
    $formWasClosed = $true
    Require-State `
        ($toolbarA.IsDisposed -and
         $null -eq (Read-Field $workspaceA "Measurement")) `
        "Cerrar la ventana no dispuso la última sesión de medición."
    $report.Add(
        "PASS - cerrar PdfViewerForm libera la última sesión y sus controles")

    Require-Identity $fixtureA $identityA
    Require-Identity $fixtureB $identityB
    $report.Add(
        "PASS - SHA-256, longitud y fecha de los PDF originales intactos")
    $report.Add(
        "EXE_SHA256=" +
        (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash)
    $report.Add("CAPTURA=$screenshotPath")
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
            $form.Close()
            Pump-Ui 100
        }
        finally {
            $form.Dispose()
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
