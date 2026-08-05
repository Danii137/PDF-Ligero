param()

$ErrorActionPreference = "Stop"

# PDFLigero.exe targets .NET Framework. PowerShell 7+ runs on modern .NET, where
# BinaryFormatter has been removed and PdfiumViewer cannot be initialized.
# Relaunch in the inbox Windows PowerShell host so this exercises the same
# runtime as the application.
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
$fixtureNormal = Join-Path $runDir "normal.pdf"
$fixtureProtected = Join-Path $runDir "protegido.pdf"
$fixtureCancel = Join-Path $runDir "protegido-cancelar.pdf"
$fixtureDamaged = Join-Path $runDir "danado.pdf"
$captureProtected = Join-Path $runDir "01-modo-protegido.png"
$captureNormal = Join-Path $runDir "02-pestana-normal.png"
$captureCompact = Join-Path $runDir "03-modo-protegido-900x620.png"
$reportPath = Join-Path $runDir "qa-report.txt"

$QaPassword = "clave-qa"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Falta el ejecutable de producción: $exePath"
}

New-Item -ItemType Directory -Force -Path $recoveryDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.AppContext]::SetSwitch(
    "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization",
    $true)

# El diálogo de contraseña es modal. Automatizarlo desde un scriptblock de
# PowerShell no es fiable dentro del bucle modal, así que el respondedor se
# compila en C# y actúa como lo haría una persona: busca el cuadro de texto y
# pulsa el botón.
Add-Type -ReferencedAssemblies @("System.Windows.Forms", "System.Drawing") `
    -TypeDefinition @"
using System;
using System.Windows.Forms;

public static class QaPasswordResponder
{
    private static Timer timer;

    public static string WrongPassword;
    public static string CorrectPassword;
    public static int WrongAttemptsRemaining;
    public static bool CancelInstead;
    public static int SeenCount;
    public static int SeenWithRetryNotice;
    public static string LastError;

    public static void Start()
    {
        Stop();
        SeenCount = 0;
        SeenWithRetryNotice = 0;
        LastError = null;
        timer = new Timer();
        timer.Interval = 40;
        timer.Tick += OnTick;
        timer.Start();
    }

    public static void Stop()
    {
        if (timer != null)
        {
            timer.Stop();
            timer.Tick -= OnTick;
            timer.Dispose();
            timer = null;
        }
    }

    private static void OnTick(object sender, EventArgs e)
    {
        try
        {
            Form prompt = FindPrompt();
            if (prompt == null)
            {
                return;
            }

            SeenCount++;
            if (HasVisibleRetryNotice(prompt))
            {
                SeenWithRetryNotice++;
            }

            if (CancelInstead)
            {
                Button cancel = FindButton(prompt, "Cancelar");
                if (cancel != null)
                {
                    cancel.PerformClick();
                }

                return;
            }

            TextBox box = FindTextBox(prompt);
            Button open = FindButton(prompt, "Abrir");
            if (box == null || open == null)
            {
                LastError = "El diálogo no tiene campo de texto o botón Abrir.";
                return;
            }

            string password;
            if (WrongAttemptsRemaining > 0)
            {
                WrongAttemptsRemaining--;
                password = WrongPassword;
            }
            else
            {
                password = CorrectPassword;
            }

            box.Text = password;
            if (!open.Enabled)
            {
                LastError = "El botón Abrir sigue deshabilitado con texto escrito.";
                return;
            }

            open.PerformClick();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private static Form FindPrompt()
    {
        foreach (Form candidate in Application.OpenForms)
        {
            if (candidate.GetType().Name == "PdfPasswordPromptForm" &&
                candidate.Visible)
            {
                return candidate;
            }
        }

        Form active = Form.ActiveForm;
        if (active != null &&
            active.GetType().Name == "PdfPasswordPromptForm")
        {
            return active;
        }

        return null;
    }

    private static bool HasVisibleRetryNotice(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Label label = child as Label;
            if (label != null &&
                label.Visible &&
                label.Text.IndexOf("no es correcta", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (HasVisibleRetryNotice(child))
            {
                return true;
            }
        }

        return false;
    }

    private static TextBox FindTextBox(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            TextBox box = child as TextBox;
            if (box != null)
            {
                return box;
            }

            TextBox nested = FindTextBox(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Button FindButton(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            Button button = child as Button;
            if (button != null && button.Text == text)
            {
                return button;
            }

            Button nested = FindButton(child, text);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
"@

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
    param([object]$Target, [string]$Name)

    $field = $Target.GetType().GetField($Name, $instanceFlags)
    if ($null -eq $field) {
        throw "No existe el campo $Name en $($Target.GetType().FullName)."
    }

    return $field.GetValue($Target)
}

function Read-WorkspaceField {
    param([object]$Workspace, [string]$Name)

    $field = $Workspace.GetType().GetField($Name, $instanceFlags)
    if ($null -eq $field) {
        throw "No existe el campo $Name en el workspace."
    }

    return $field.GetValue($Workspace)
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

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
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
    param([bool]$Condition, [string]$FailureMessage)

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
    param([string]$Path, [object]$Expected)

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
        [int]$PageCount,
        [string]$UserPassword
    )

    $page = [iTextSharp.text.PageSize]::A4
    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $document = New-Object iTextSharp.text.Document($page, 0, 0, 0, 0)
    try {
        $writer = [iTextSharp.text.pdf.PdfWriter]::GetInstance(
            $document,
            $stream)
        if (-not [string]::IsNullOrEmpty($UserPassword)) {
            $writer.SetEncryption(
                [System.Text.Encoding]::ASCII.GetBytes($UserPassword),
                [System.Text.Encoding]::ASCII.GetBytes("propietario-qa"),
                [iTextSharp.text.pdf.PdfWriter]::ALLOW_PRINTING,
                [iTextSharp.text.pdf.PdfWriter]::ENCRYPTION_AES_128)
        }

        $document.AddTitle("Fixture endurecimiento $Label") | Out-Null
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
            $canvas.ShowText("ENDURECIMIENTO / VISOR QA / $Label")
            $canvas.SetFontAndSize($font, 12)
            $canvas.SetTextMatrix(96, $page.Height - 205)
            $canvas.ShowText("PAGINA " + ($index + 1) + " / ORIGINAL INTACTO")
            $canvas.EndText()
            $canvas.RestoreState()
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

function Capture-Window {
    param([System.Windows.Forms.Form]$Window, [string]$Path)

    $Window.Activate() | Out-Null
    $Window.BringToFront()
    $Window.TopMost = $true
    Pump-Ui 240

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
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
        $Window.TopMost = $false
    }

    Require-State `
        ((Get-Item -LiteralPath $Path).Length -gt 12000) `
        "La captura está vacía o incompleta: $Path"
}

function Select-TabByPath {
    param([object]$ViewerForm, [string]$Path)

    $workspaces = Read-Field $ViewerForm "workspaces"
    foreach ($candidate in $workspaces) {
        if ((Read-WorkspaceField $candidate "Path") -eq $Path) {
            $tabs = Read-Field $ViewerForm "documentTabs"
            $tabs.SelectedTab = (Read-WorkspaceField $candidate "TabPage")
            Pump-Ui 200
            return $candidate
        }
    }

    throw "No hay ninguna pestaña para $Path."
}

function Get-ToolState {
    param([object]$ViewerForm)

    return [PSCustomObject]@{
        Texto = (Read-Field $ViewerForm "contentEditToolButton").Enabled
        Ocr = (Read-Field $ViewerForm "ocrToolButton").Enabled
        Firmar = (Read-Field $ViewerForm "signToolButton").Enabled
        Comparar = (Read-Field $ViewerForm "compareToolButton").Enabled
        Buscar = (Read-Field $ViewerForm "searchToolButton").Enabled
        Medir = (Read-Field $ViewerForm "measureToolButton").Enabled
        Combinar = (Read-Field $ViewerForm "mergeToolButton").Enabled
        GuardarCopia = (Read-Field $ViewerForm "saveCopyMenuItem").Enabled
        Imprimir = (Read-Field $ViewerForm "printMenuItem").Enabled
        Organizar = (Read-Field $ViewerForm "organizePagesMenuItem").Enabled
        Marcadores = (Read-Field $ViewerForm "editBookmarksMenuItem").Enabled
        Rotulo = (Read-Field $ViewerForm "documentEyebrowLabel").Text
    }
}

try {
    Write-Host "STEP 01 entorno"
    [Environment]::SetEnvironmentVariable(
        "PDFLIGERO_RECOVERY_ROOT",
        $recoveryDir)
    [Environment]::CurrentDirectory = $outputDir

    [Reflection.Assembly]::LoadFrom(
        (Join-Path $packages "BouncyCastle.1.8.9\lib\BouncyCastle.Crypto.dll")) |
        Out-Null
    [Reflection.Assembly]::LoadFrom(
        (Join-Path $packages "iTextSharp.5.5.13.3\lib\itextsharp.dll")) |
        Out-Null

    New-FixturePdf $fixtureNormal "NORMAL" 2 $null
    New-FixturePdf $fixtureProtected "PROTEGIDO" 1 $QaPassword
    New-FixturePdf $fixtureCancel "CANCELAR" 1 $QaPassword
    [System.IO.File]::WriteAllText($fixtureDamaged, "Esto no es un PDF.")
    Write-Host "STEP 02 fixtures"

    $identityNormal = Capture-Identity $fixtureNormal
    $identityProtected = Capture-Identity $fixtureProtected
    $identityCancel = Capture-Identity $fixtureCancel

    [Reflection.Assembly]::LoadFrom(
        (Join-Path $outputDir "PdfiumViewer.dll")) |
        Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($exePath)
    $viewerType = $assembly.GetType("FirmaAutomatica.PdfViewerForm", $true)
    $promptType = $assembly.GetType(
        "FirmaAutomatica.PdfPasswordPromptForm",
        $true)
    Require-State `
        ($null -ne $promptType) `
        "No existe el diálogo propio de contraseña."
    Write-Host "STEP 03 ensamblado"

    $constructor = $viewerType.GetConstructors($instanceFlags) |
        Where-Object {
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -like "IEnumerable*"
        } |
        Select-Object -First 1
    Require-State ($null -ne $constructor) "No se encontró el constructor del visor."

    [System.Windows.Forms.Application]::EnableVisualStyles()
    $constructorArguments = New-Object object[] 1
    $constructorArguments[0] = [string[]]@(
        $fixtureNormal,
        $fixtureProtected,
        $fixtureCancel)
    $form = $constructor.Invoke($constructorArguments)
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
    Write-Host "STEP 04 visor mostrado"

    Wait-Until `
        {
            $tabs = Read-Field $form "documentTabs"
            $active = Read-Field $form "activeWorkspace"
            $tabs.TabPages.Count -eq 3 -and
                $null -ne $active -and
                [bool](Read-WorkspaceField $active "IsLoaded")
        } `
        30000 `
        "El visor no cargó la primera pestaña."

    $normalState = Get-ToolState $form
    Require-State `
        ($normalState.Rotulo -eq "DOCUMENTO ACTIVO") `
        "El PDF normal no muestra DOCUMENTO ACTIVO: $($normalState.Rotulo)"
    Require-State `
        ($normalState.Texto -and $normalState.Ocr -and $normalState.Firmar) `
        "Las herramientas de edición deberían estar activas en un PDF normal."
    $report.Add("PASS - un PDF normal abre sin pedir contraseña y con todo activo")
    Write-Host "STEP 05 pestaña normal"

    # --- Contraseña incorrecta dos veces y luego la correcta -----------------
    [QaPasswordResponder]::WrongPassword = "incorrecta"
    [QaPasswordResponder]::CorrectPassword = $QaPassword
    [QaPasswordResponder]::WrongAttemptsRemaining = 2
    [QaPasswordResponder]::CancelInstead = $false
    [QaPasswordResponder]::Start()

    # Cambiar de pestaña dispara la apertura y, con ella, el diálogo modal.
    $tabs = Read-Field $form "documentTabs"
    $workspaces = Read-Field $form "workspaces"
    $protectedWorkspace = $null
    foreach ($candidate in $workspaces) {
        if ((Read-WorkspaceField $candidate "Path") -eq $fixtureProtected) {
            $protectedWorkspace = $candidate
        }
    }

    Require-State `
        ($null -ne $protectedWorkspace) `
        "No se creó la pestaña del PDF protegido."

    $tabs.SelectedTab = (Read-WorkspaceField $protectedWorkspace "TabPage")
    Pump-Ui 400
    Wait-Until `
        { [bool](Read-WorkspaceField $protectedWorkspace "IsLoaded") } `
        30000 `
        "El PDF protegido no llegó a abrirse con la contraseña correcta."
    [QaPasswordResponder]::Stop()

    Require-State `
        ($null -eq [QaPasswordResponder]::LastError) `
        "El diálogo de contraseña falló: $([QaPasswordResponder]::LastError)"
    Require-State `
        ([QaPasswordResponder]::SeenCount -ge 3) `
        "El diálogo propio no se mostró en los dos fallos y el acierto."
    Require-State `
        ([QaPasswordResponder]::SeenWithRetryNotice -ge 1) `
        "El reintento no mostró el aviso de contraseña incorrecta."
    $report.Add(
        "PASS - diálogo propio en español, aviso de contraseña incorrecta y " +
        "apertura al acertar")
    Write-Host "STEP 06 contraseña aceptada"

    # --- Modo protegido ------------------------------------------------------
    Require-State `
        ([bool](Read-WorkspaceField $protectedWorkspace "IsPasswordProtected")) `
        "La pestaña abierta con contraseña no quedó marcada como protegida."

    # La activación de la pestaña aún puede estar en curso justo después de que
    # IsLoaded se ponga a true.
    Pump-Ui 400
    $protectedState = Get-ToolState $form
    Require-State `
        ($protectedState.Rotulo -eq "DOCUMENTO PROTEGIDO") `
        "El rótulo no indica DOCUMENTO PROTEGIDO: $($protectedState.Rotulo)"
    $shouldBeOff = @("Texto", "Ocr", "Firmar", "Comparar", "Organizar", "Marcadores")
    $stillOn = @($shouldBeOff | Where-Object { $protectedState.$_ })
    Require-State `
        ($stillOn.Count -eq 0) `
        ("Siguen activas en modo protegido: " + ($stillOn -join ", "))

    $shouldStayOn = @(
        "Buscar", "Medir", "Combinar", "GuardarCopia", "Imprimir")
    $wronglyOff = @($shouldStayOn | Where-Object { -not $protectedState.$_ })
    Require-State `
        ($wronglyOff.Count -eq 0) `
        ("El modo protegido apagó de más: " + ($wronglyOff -join ", "))

    $thumbnails = Read-WorkspaceField $protectedWorkspace "Thumbnails"
    Require-State `
        (-not [bool]$thumbnails.PageOperationsEnabled) `
        "Las miniaturas permiten operar páginas en un PDF protegido."

    $tabText = (Read-WorkspaceField $protectedWorkspace "TabPage").Text
    $tabTip = (Read-WorkspaceField $protectedWorkspace "TabPage").ToolTipText
    Require-State `
        ($tabTip -like "*solo lectura*") `
        "La pestaña protegida no explica que es de solo lectura: $tabTip"
    $report.Add(
        "PASS - modo protegido: edición apagada, lectura/impresión/medición " +
        "disponibles y pestaña señalizada")
    Capture-Window $form $captureProtected
    Write-Host "STEP 07 modo protegido"

    # --- Los atajos tampoco deben poder editar -------------------------------
    $recoveryBefore = @(Get-ChildItem -LiteralPath $recoveryDir -Recurse -File -ErrorAction SilentlyContinue).Count

    foreach ($methodName in @(
        "BeginTextEditSelection",
        "FillActivePdfForm",
        "EditActiveBookmarks",
        "BeginPlanComparison")) {
        $method = $viewerType.GetMethods($instanceFlags) |
            Where-Object { $_.Name -eq $methodName -and $_.GetParameters().Count -eq 0 } |
            Select-Object -First 1
        Require-State ($null -ne $method) "No existe $methodName."
        $method.Invoke($form, @()) | Out-Null
        Pump-Ui 120
    }

    Require-State `
        ($null -eq (Read-Field $form "comparisonSurface")) `
        "Un atajo abrió la comparación sobre un PDF protegido."
    $selection = Read-WorkspaceField $protectedWorkspace "TextEditSelection"
    Require-State `
        ($null -eq $selection) `
        "Un atajo creó el selector de texto sobre un PDF protegido."

    $recoveryAfter = @(Get-ChildItem -LiteralPath $recoveryDir -Recurse -File -ErrorAction SilentlyContinue).Count
    Require-State `
        ($recoveryAfter -eq $recoveryBefore) `
        "Un atajo reservó espacio en Recovery sobre un PDF protegido."
    $report.Add(
        "PASS - Ctrl+E, formularios, marcadores y comparación no hacen nada y " +
        "no tocan Recovery")
    Write-Host "STEP 08 atajos bloqueados"

    # --- Volver a la pestaña normal restaura todo ----------------------------
    $normalWorkspace = Select-TabByPath $form $fixtureNormal
    $restoredState = Get-ToolState $form
    Require-State `
        ($restoredState.Rotulo -eq "DOCUMENTO ACTIVO") `
        "Al volver al PDF normal el rótulo no se restauró."
    Require-State `
        ($restoredState.Texto -and
         $restoredState.Ocr -and
         $restoredState.Firmar -and
         $restoredState.Comparar) `
        "Al volver al PDF normal las herramientas no se reactivaron."
    Require-State `
        (-not [bool](Read-WorkspaceField $normalWorkspace "IsPasswordProtected")) `
        "El PDF normal quedó marcado como protegido."
    $report.Add("PASS - alternar entre protegido y normal restaura el estado")
    Capture-Window $form $captureNormal
    Write-Host "STEP 09 alternancia"

    # --- Cancelar el diálogo -------------------------------------------------
    [QaPasswordResponder]::CancelInstead = $true
    [QaPasswordResponder]::Start()

    $cancelWorkspace = $null
    foreach ($candidate in $workspaces) {
        if ((Read-WorkspaceField $candidate "Path") -eq $fixtureCancel) {
            $cancelWorkspace = $candidate
        }
    }

    Require-State ($null -ne $cancelWorkspace) "No se creó la pestaña a cancelar."
    $tabs.SelectedTab = (Read-WorkspaceField $cancelWorkspace "TabPage")
    Wait-Until `
        { [bool](Read-WorkspaceField $cancelWorkspace "LoadFailed") } `
        20000 `
        "Cancelar el diálogo no marcó la pestaña como no cargada."
    [QaPasswordResponder]::Stop()

    Require-State `
        (-not [bool](Read-WorkspaceField $cancelWorkspace "IsLoaded")) `
        "La pestaña cancelada figura como cargada."
    Require-State `
        ([bool](Read-WorkspaceField $cancelWorkspace "PasswordPromptCancelled")) `
        "No se registró que el usuario canceló la contraseña."
    Require-State `
        ((Read-WorkspaceField $cancelWorkspace "TabPage").Text -like "! *") `
        "La pestaña cancelada no conserva la marca de fallo."

    # La prueba de la fuga: el archivo debe poder renombrarse acto seguido.
    $renamed = $fixtureCancel + ".movido"
    [System.IO.File]::Move($fixtureCancel, $renamed)
    [System.IO.File]::Move($renamed, $fixtureCancel)
    $report.Add(
        "PASS - cancelar deja la pestaña marcada, sin error, y el archivo se " +
        "puede renombrar de inmediato")
    Write-Host "STEP 10 cancelación"

    # --- Layout compacto -----------------------------------------------------
    Select-TabByPath $form $fixtureProtected | Out-Null
    $form.Size = [System.Drawing.Size]::new(900, 620)
    Pump-Ui 350
    Capture-Window $form $captureCompact
    $report.Add("PASS - modo protegido legible a 900x620")
    Write-Host "STEP 11 layout compacto"

    # --- Cierre --------------------------------------------------------------
    $form.Close()
    $formWasClosed = $true
    Pump-Ui 600

    Require-Identity $fixtureNormal $identityNormal
    Require-Identity $fixtureProtected $identityProtected
    Require-Identity $fixtureCancel $identityCancel
    $report.Add("PASS - SHA-256, longitud y fecha de los tres fixtures intactos")

    $leftovers = @(Get-ChildItem -LiteralPath $runDir -Filter "*.tmp" -Recurse -ErrorAction SilentlyContinue)
    Require-State `
        ($leftovers.Count -eq 0) `
        "Quedaron temporales tras la sesión."
    $report.Add("PASS - sin temporales residuales")
    Write-Host "STEP 12 cierre"

    $report.Add(
        "EXE_SHA256=" +
        (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash)
    $report.Add("CAPTURA_PROTEGIDO=$captureProtected")
    $report.Add("CAPTURA_NORMAL=$captureNormal")
    $report.Add("CAPTURA_COMPACTA=$captureCompact")
    $report.Add("RESULTADO GLOBAL: PASS")
}
catch {
    $report.Add("RESULTADO GLOBAL: FAIL")
    $report.Add($_.Exception.ToString())
    $report | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "REPORT=$reportPath"
    throw
}
finally {
    [QaPasswordResponder]::Stop()

    if ($null -ne $form -and -not $formWasClosed) {
        try {
            $form.Close()
        }
        catch {
        }
    }

    if ($null -ne $form) {
        try {
            $form.Dispose()
        }
        catch {
        }
    }

    [Environment]::SetEnvironmentVariable(
        "PDFLIGERO_RECOVERY_ROOT",
        $previousRecoveryRoot)
    [Environment]::CurrentDirectory = $previousCurrentDirectory
}

$report | Set-Content -LiteralPath $reportPath -Encoding UTF8
Set-Content `
    -LiteralPath (Join-Path $validationDir "latest-run.txt") `
    -Value $runDir `
    -Encoding UTF8
$report | ForEach-Object { Write-Host $_ }
Write-Host "REPORT=$reportPath"
exit 0
