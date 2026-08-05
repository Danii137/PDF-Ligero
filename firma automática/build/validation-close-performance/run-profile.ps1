param(
    [int]$Iterations = 3
)

$ErrorActionPreference = "Stop"
$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$output = Join-Path $root "build\output"
$exe = Join-Path $output "PDFLigero.exe"
$run = Join-Path $validation ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
$recovery = Join-Path $run "recovery"
$csv = Join-Path $run "stage-results.csv"
$report = Join-Path $run "qa-report.txt"

New-Item -ItemType Directory -Force $run,$recovery | Out-Null
if (-not (Test-Path -LiteralPath $exe)) { throw "Falta $exe" }
if (Get-Process PDFLigero,FirmaAutomatica -ErrorAction SilentlyContinue) {
    throw "Cierra PDF Ligero antes del perfilado."
}

$fixtures = @(
    (Join-Path $root "build\validation-plan-comparison-engine\run\plano-base-vectorial.pdf"),
    (Join-Path $root "build\insert-service-tests\run\long-81-pages.pdf"),
    ((Get-ChildItem -LiteralPath (Join-Path $root "build\validation-ocr-ui\run") -Filter "integracion OCR*.pdf" | Select-Object -First 1).FullName),
    (Join-Path $root "build\validation-phase1-large\fixture-scanned-a4-32mb.pdf")
)
foreach ($fixture in $fixtures) {
    if (-not (Test-Path -LiteralPath $fixture)) { throw "Falta fixture: $fixture" }
}

$env:PDFLIGERO_RECOVERY_ROOT = $recovery
$env:PATH = $output + ";" + $env:PATH
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Reflection.Assembly]::LoadFrom((Join-Path $output "itextsharp.dll")) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $output "BouncyCastle.Crypto.dll")) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $output "PdfiumViewer.dll")) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom($exe)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

$flags = [Reflection.BindingFlags] "Instance,Public,NonPublic"
$staticFlags = [Reflection.BindingFlags] "Static,Public,NonPublic"
$formType = $assembly.GetType("FirmaAutomatica.PdfViewerForm", $true)
$workspaceField = $formType.GetField("workspaces", $flags)
$dictField = $formType.GetField("workspaceByPath", $flags)
$activeField = $formType.GetField("activeWorkspace", $flags)
$tabsField = $formType.GetField("documentTabs", $flags)
$toolTipField = $formType.GetField("toolTip", $flags)
$closingAllField = $formType.GetField("closingAll", $flags)
$formClosingMethod = $formType.GetMethod("PdfViewerForm_FormClosing", $flags)
$formClosedMethod = $formType.GetMethod("PdfViewerForm_FormClosed", $flags)
$measureToolMethod = $formType.GetMethod("MeasureToolButton_Click", $flags)
$disposeMeasurementMethod = $formType.GetMethod("DisposeWorkspaceMeasurement", $flags)
$refreshEmptyMethod = $formType.GetMethod("RefreshEmptyState", $flags)
$refreshToolsMethod = $formType.GetMethod("RefreshToolAvailability", $flags)
$releaseLeaseMethod = $formType.GetMethod("ReleaseSavedCopyVerificationLease", $staticFlags)

$script:rows = New-Object Collections.Generic.List[object]

function Read-Field($object, [string]$name) {
    return $object.GetType().GetField($name, $script:flags).GetValue($object)
}

function Write-Field($object, [string]$name, $value) {
    $object.GetType().GetField($name, $script:flags).SetValue($object, $value)
}

function Invoke-Stage([int]$iteration, [string]$workspaceName, [string]$stage, [scriptblock]$action) {
    [GC]::KeepAlive($action)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $action
    $timer.Stop()
    $script:rows.Add([pscustomobject]@{
        Iteration = $iteration
        Workspace = $workspaceName
        Stage = $stage
        Milliseconds = [Math]::Round($timer.Elapsed.TotalMilliseconds, 3)
    })
}

function Invoke-Private($object, [string]$name, [object[]]$arguments) {
    return $object.GetType().GetMethod($name, $script:flags).Invoke($object, $arguments)
}

function Remove-Handler($target, [string]$eventName, $handler) {
    if ($null -eq $target -or $null -eq $handler) { return }
    $eventInfo = $target.GetType().GetEvent($eventName, $script:flags)
    if ($null -ne $eventInfo) { $eventInfo.RemoveEventHandler($target, $handler) }
}

function Remove-MethodHandler($target, [string]$eventName, $owner, [string]$methodName) {
    if ($null -eq $target) { return }
    $eventInfo = $target.GetType().GetEvent($eventName, $script:flags)
    $methodInfo = $owner.GetType().GetMethod($methodName, $script:flags)
    if ($null -ne $eventInfo -and $null -ne $methodInfo) {
        $handler = [Delegate]::CreateDelegate($eventInfo.EventHandlerType, $owner, $methodInfo)
        $eventInfo.RemoveEventHandler($target, $handler)
    }
}

function New-ViewerForm([string[]]$paths) {
    $ctor = $script:formType.GetConstructor(
        $script:flags,
        $null,
        [Type[]]@([Collections.Generic.IEnumerable[string]]),
        $null)
    $form = [Windows.Forms.Form]$ctor.Invoke([object[]]@(,[string[]]$paths))
    $form.Show()
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    do {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 15
        $list = $script:workspaceField.GetValue($form)
        $items = @($list)
        $loaded = $items.Count -eq $paths.Count -and $items.Count -gt 0 -and [bool](Read-Field $items[0] "IsLoaded")
    } while (-not $loaded -and [DateTime]::UtcNow -lt $deadline)
    if (-not $loaded) {
        $form.Dispose()
        throw "El visor no preparó las pestañas."
    }
    for ($i = 0; $i -lt 12; $i++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 }
    return $form
}

function Profile-WorkspaceCleanup($form, $workspace, [int]$iteration) {
    $name = [string](Read-Field $workspace "DisplayName")
    $tabs = [Windows.Forms.TabControl]$script:tabsField.GetValue($form)
    $tab = [Windows.Forms.TabPage](Read-Field $workspace "TabPage")

    Invoke-Stage $iteration $name "TabControl.Remove" { $tabs.TabPages.Remove($tab) }
    Invoke-Stage $iteration $name "Measurement.Dispose" {
        $script:disposeMeasurementMethod.Invoke($form, [object[]]@($workspace)) | Out-Null
    }
    Invoke-Stage $iteration $name "Workspace bookkeeping" {
        Write-Field $workspace "IsDisposed" $true
        $path = [string](Read-Field $workspace "Path")
        $dict = $script:dictField.GetValue($form)
        $dict.GetType().GetMethod("Remove", [Type[]]@([string])).Invoke($dict, [object[]]@($path)) | Out-Null
        $list = $script:workspaceField.GetValue($form)
        $list.GetType().GetMethod("Remove").Invoke($list, [object[]]@($workspace)) | Out-Null
        if ([object]::ReferenceEquals($script:activeField.GetValue($form), $workspace)) {
            $script:activeField.SetValue($form, $null)
        }
    }
    Invoke-Stage $iteration $name "RectangleZoom.Dispose" {
        $zoom = Read-Field $workspace "RectangleZoom"
        if ($null -ne $zoom) { $zoom.Dispose(); Write-Field $workspace "RectangleZoom" $null }
    }
    Invoke-Stage $iteration $name "Event unhook + tooltips" {
        $viewer = Read-Field $workspace "Viewer"
        $renderer = $viewer.Renderer
        $thumbs = Read-Field $workspace "Thumbnails"
        $tree = Read-Field $workspace "BookmarksTree"
        Remove-Handler $renderer "Scroll" (Read-Field $workspace "ScrollHandler")
        Remove-Handler $thumbs "PageSelected" (Read-Field $workspace "ThumbnailSelectionHandler")
        Remove-Handler $thumbs "PdfFilesInsertRequested" (Read-Field $workspace "PdfInsertHandler")
        Remove-Handler $thumbs "PagesReorderRequested" (Read-Field $workspace "PageReorderHandler")
        Remove-Handler $thumbs "PageOperationRequested" (Read-Field $workspace "PageOperationHandler")
        Remove-Handler $tree "NodeMouseClick" (Read-Field $workspace "BookmarkSelectionHandler")
        foreach ($controlName in @("TabPage","Viewer","NavigationPanel","NavigationHeader","BookmarksTree")) {
            $control = Read-Field $workspace $controlName
            Remove-MethodHandler $control "DragEnter" $form "PdfDragEnter"
            Remove-MethodHandler $control "DragDrop" $form "PdfDragDrop"
        }
        Remove-MethodHandler $renderer "DragEnter" $form "PdfDragEnter"
        Remove-MethodHandler $renderer "DragDrop" $form "PdfDragDrop"
        $toolTip = [Windows.Forms.ToolTip]$script:toolTipField.GetValue($form)
        foreach ($controlName in @("PagesButton","BookmarksButton","CollapseNavigationButton","Thumbnails")) {
            $toolTip.SetToolTip([Windows.Forms.Control](Read-Field $workspace $controlName), $null)
        }
    }
    Invoke-Stage $iteration $name "Thumbnails.ClearDocument" {
        $thumbs = Read-Field $workspace "Thumbnails"
        $thumbs.GetType().GetMethod("ClearDocument").Invoke($thumbs, $null) | Out-Null
    }
    Invoke-Stage $iteration $name "TabPage.Dispose" { $tab.Dispose() }
    Invoke-Stage $iteration $name "Document.Dispose" {
        $document = Read-Field $workspace "Document"
        if ($null -ne $document) { $document.Dispose(); Write-Field $workspace "Document" $null }
    }
    Invoke-Stage $iteration $name "EditSession cleanup" {
        $session = Read-Field $workspace "EditSession"
        if ($null -ne $session) {
            $method = if ([bool](Read-Field $workspace "DeleteRecoveryOnClose")) { "DeleteRecovery" } else { "PreserveRecovery" }
            $session.GetType().GetMethod($method).Invoke($session, $null) | Out-Null
            Write-Field $workspace "EditSession" $null
        }
    }
    Invoke-Stage $iteration $name "Saved lease cleanup" {
        $script:releaseLeaseMethod.Invoke($null, [object[]]@($workspace)) | Out-Null
    }
    Invoke-Stage $iteration $name "CloseWorkspace UI refresh" {
        $script:refreshEmptyMethod.Invoke($form, $null) | Out-Null
        $script:refreshToolsMethod.Invoke($form, $null) | Out-Null
    }
}

try {
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $form = New-ViewerForm $fixtures
        try {
            $script:measureToolMethod.Invoke($form, [object[]]@($form,[EventArgs]::Empty)) | Out-Null
            [Windows.Forms.Application]::DoEvents()
            Invoke-Stage $iteration "FORM" "FormClosing handler" {
                $closingArgs = [Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::UserClosing, $false)
                $script:formClosingMethod.Invoke($form, [object[]]@($form,$closingArgs)) | Out-Null
                if ($closingArgs.Cancel) { throw "Cierre cancelado inesperadamente." }
            }
            $script:closingAllField.SetValue($form, $true)
            foreach ($workspace in @($script:workspaceField.GetValue($form))) {
                Profile-WorkspaceCleanup $form $workspace $iteration
            }
            Invoke-Stage $iteration "FORM" "FormClosed residual" {
                $closedArgs = [Windows.Forms.FormClosedEventArgs]::new([Windows.Forms.CloseReason]::UserClosing)
                $script:formClosedMethod.Invoke($form, [object[]]@($form,$closedArgs)) | Out-Null
            }
        }
        finally {
            Invoke-Stage $iteration "FORM" "Form.Dispose residual" { $form.Dispose() }
        }
    }

    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $form = New-ViewerForm $fixtures
        try {
            Invoke-Stage $iteration "FORM" "FormClosing+FormClosed actual" { $form.Close() }
        }
        finally {
            Invoke-Stage $iteration "FORM" "Form.Dispose after Close" { $form.Dispose() }
        }
    }

    $brokerType = $assembly.GetType("FirmaAutomatica.ViewerInstanceBroker", $true)
    $tryStart = $brokerType.GetMethod("TryStart", $staticFlags)
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $arguments = [object[]]@([string[]]@(), $null)
        $primary = [bool]$tryStart.Invoke($null, $arguments)
        if (-not $primary -or $null -eq $arguments[1]) { throw "No se pudo aislar el broker." }
        Start-Sleep -Milliseconds 80
        $broker = $arguments[1]
        Invoke-Stage $iteration "PROCESS" "ViewerInstanceBroker.Dispose" { $broker.Dispose() }
    }
}
finally {
    Remove-Item Env:PDFLIGERO_RECOVERY_ROOT -ErrorAction SilentlyContinue
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 $csv
$summary = $rows | Group-Object Workspace,Stage | ForEach-Object {
    $values = @($_.Group | ForEach-Object { [double]$_.Milliseconds } | Sort-Object)
    [pscustomobject]@{
        Workspace = $_.Group[0].Workspace
        Stage = $_.Group[0].Stage
        MedianMs = $values[[int][Math]::Floor(($values.Count - 1) / 2)]
        MaximumMs = ($values | Measure-Object -Maximum).Maximum
        Samples = $values.Count
    }
} | Sort-Object MedianMs -Descending

$lines = New-Object Collections.Generic.List[string]
$lines.Add("PDF Ligero - perfil de cierre multipestaña")
$lines.Add("Run: $run")
$lines.Add("Iteraciones: $Iterations")
$lines.Add("")
$lines.Add("Medianas por etapa (ms), orden descendente:")
foreach ($item in $summary) {
    $lines.Add(("{0} | {1} | median={2:N3} | max={3:N3} | n={4}" -f $item.Workspace,$item.Stage,$item.MedianMs,$item.MaximumMs,$item.Samples))
}
$lines.Add("")
$lines.Add("Procesos residuales: " + @((Get-Process PDFLigero,FirmaAutomatica -ErrorAction SilentlyContinue)).Count)
[IO.File]::WriteAllLines($report, $lines, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $validation "latest-run.txt"), $run, [Text.UTF8Encoding]::new($false))

Get-Content $report
