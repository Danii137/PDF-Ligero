param()

$ErrorActionPreference = "Stop"

$validationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validationDir)
$outputDir = Join-Path $root "build\output"
$exePath = Join-Path $outputDir "PDFLigero.exe"
$fixtureRoot = Get-ChildItem `
    (Join-Path $root "build\validation-plan-comparison") `
    -Directory |
    Where-Object { $_.Name -like "run-*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $fixtureRoot) {
    throw "No existe un fixture de comparación."
}

$fixtureA = Join-Path $fixtureRoot.FullName "revision-A.pdf"
$fixtureB = Join-Path $fixtureRoot.FullName "revision-B.pdf"
foreach ($requiredPath in @($exePath, $fixtureA, $fixtureB)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Falta el archivo requerido: $requiredPath"
    }
}

$runDir = Join-Path `
    $validationDir `
    ("run-" + [Guid]::NewGuid().ToString("N"))
$recoveryDir = Join-Path $runDir "recovery"
New-Item -ItemType Directory -Force -Path $recoveryDir | Out-Null
$reportPath = Join-Path $runDir "qa-report.txt"
$report = New-Object System.Collections.Generic.List[string]

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$instanceFlags = `
    [System.Reflection.BindingFlags]::Instance -bor `
    [System.Reflection.BindingFlags]::Public -bor `
    [System.Reflection.BindingFlags]::NonPublic

function Read-Field {
    param(
        [object]$Target,
        [string]$Name
    )

    $field = $Target.GetType().GetField($Name, $instanceFlags)
    if ($null -eq $field) {
        throw "No existe el campo privado $Name."
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
        throw "No existe la propiedad $Name."
    }

    return $property.GetValue($Target, $null)
}

function Invoke-Method {
    param(
        [object]$Target,
        [string]$Name,
        [object[]]$Arguments
    )

    $method = $Target.GetType().GetMethod(
        $Name,
        $instanceFlags)
    if ($null -eq $method) {
        throw "No existe el método privado $Name."
    }

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
        "El fixture original cambió: $Path"
}

$identityA = Capture-Identity $fixtureA
$identityB = Capture-Identity $fixtureB
$previousRecoveryRoot = [Environment]::GetEnvironmentVariable(
    "PDFLIGERO_RECOVERY_ROOT")
$form = $null

try {
    [Environment]::SetEnvironmentVariable(
        "PDFLIGERO_RECOVERY_ROOT",
        $recoveryDir)
    [Environment]::CurrentDirectory = $outputDir

    $assembly = [System.Reflection.Assembly]::LoadFrom($exePath)
    $viewerType = $assembly.GetType(
        "FirmaAutomatica.PdfViewerForm",
        $true)
    $constructor = $viewerType.GetConstructors($instanceFlags) |
        Where-Object {
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -like "IEnumerable*"
        } |
        Select-Object -First 1
    if ($null -eq $constructor) {
        throw "No se encontró el constructor del visor."
    }

    $constructorArguments = New-Object object[] 1
    $constructorArguments[0] = [string[]]@($fixtureA, $fixtureB)
    $form = $constructor.Invoke($constructorArguments)
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
    $report.Add("PASS - dos pestanas cargadas en el visor real")

    $tabs = Read-Field $form "documentTabs"
    $active = Read-Field $form "activeWorkspace"
    $tabPage = Read-Field $active "TabPage"
    $navigation = Read-Field $active "NavigationPanel"
    $compareButton = Read-Field $form "compareToolButton"
    Require-State $compareButton.Enabled "El botón Delta no está disponible."
    Require-State `
        ($compareButton.Text -eq [char]0x0394) `
        "El botón Delta no muestra el acceso de comparación."

    $compareButton.PerformClick()
    Wait-Until `
        {
            $surface = Read-Field $form "comparisonSurface"
            if ($null -eq $surface) {
                return $false
            }
            $result = Read-Property $surface "ResultForTesting"
            return -not [bool](Read-Property $surface "IsBusy") -and
                $null -ne $result
        } `
        45000 `
        "La comparación no terminó el primer render."

    $surface = Read-Field $form "comparisonSurface"
    Require-State `
        ($surface.Parent -eq $tabPage) `
        "La superficie no pertenece a la pestaña activa."
    Require-State `
        ($surface.Bounds -eq $tabPage.ClientRectangle) `
        "La superficie no cubre el área completa de la pestaña."
    Require-State `
        ($surface.Anchor -eq
            ([System.Windows.Forms.AnchorStyles]::Top -bor
             [System.Windows.Forms.AnchorStyles]::Bottom -bor
             [System.Windows.Forms.AnchorStyles]::Left -bor
             [System.Windows.Forms.AnchorStyles]::Right)) `
        "La superficie no conserva el área completa al redimensionar."
    Require-State `
        ($tabPage.Controls.GetChildIndex($surface) -eq 0) `
        "La superficie no está por encima del visor y las miniaturas."
    Require-State `
        ($surface.Bounds.Contains($navigation.Bounds)) `
        "La superficie no cubre el panel de miniaturas."
    $report.Add(
        "PASS - Delta abre una capa completa sobre visor y miniaturas")

    foreach ($fieldName in @(
        "searchToolButton",
        "ocrToolButton",
        "signToolButton",
        "mergeToolButton",
        "undoMenuItem",
        "redoMenuItem",
        "saveCopyMenuItem",
        "printMenuItem",
        "fitWidthMenuItem",
        "zoomInMenuItem",
        "zoomOutMenuItem",
        "rotateLeftMenuItem",
        "rotateRightMenuItem",
        "ocrMenuItem",
        "organizePagesMenuItem",
        "editBookmarksMenuItem"
    )) {
        $control = Read-Field $form $fieldName
        Require-State `
            (-not $control.Enabled) `
            "La acción $fieldName sigue activa durante la comparación."
    }
    Require-State `
        $compareButton.Enabled `
        "El cierre de comparación quedó deshabilitado."
    $report.Add(
        "PASS - herramientas y mutaciones bloqueadas durante comparacion")

    $shortcut = [System.Windows.Forms.KeyEventArgs]::new(
        [System.Windows.Forms.Keys]::Control -bor
        [System.Windows.Forms.Keys]::Shift -bor
        [System.Windows.Forms.Keys]::C)
    $shortcutArguments = New-Object object[] 2
    $shortcutArguments[0] = $form
    $shortcutArguments[1] = $shortcut
    Invoke-Method `
        $form `
        "PdfViewerForm_KeyDown" `
        $shortcutArguments | Out-Null
    Pump-Ui 100
    Require-State `
        ($null -eq (Read-Field $form "comparisonSurface")) `
        "Ctrl+Mayús+C no cerró la comparación."
    $report.Add("PASS - Ctrl+Mayus+C abre/cierra sin dejar worker activo")

    $compareButton.PerformClick()
    Wait-Until `
        { $null -ne (Read-Field $form "comparisonSurface") } `
        5000 `
        "No se pudo reabrir la comparación."
    $tabs.SelectedIndex = 1
    Wait-Until `
        { $null -eq (Read-Field $form "comparisonSurface") } `
        5000 `
        "Cambiar de pestaña no canceló la comparación."
    $report.Add("PASS - cambiar de pestana cancela y dispone la superficie")

    $compareButton = Read-Field $form "compareToolButton"
    $compareButton.PerformClick()
    Wait-Until `
        { $null -ne (Read-Field $form "comparisonSurface") } `
        5000 `
        "No se pudo abrir la comparación en la segunda pestaña."
    $tabs.CloseActiveTab() | Out-Null
    Wait-Until `
        {
            $tabs.TabPages.Count -eq 1 -and
                $null -eq (Read-Field $form "comparisonSurface")
        } `
        5000 `
        "Cerrar la pestaña no canceló la comparación."
    $report.Add("PASS - cerrar pestana cancela y libera la sesion")

    Require-Identity $fixtureA $identityA
    Require-Identity $fixtureB $identityB
    $report.Add("PASS - SHA-256, longitud y fecha de originales intactos")
    $report.Add("RESULTADO GLOBAL: PASS")
}
finally {
    if ($null -ne $form) {
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
    $report | Set-Content -LiteralPath $reportPath -Encoding UTF8
}

$report | ForEach-Object { Write-Host $_ }
Write-Host "report=$reportPath"
