param(
    [int]$WarmupIterations = 1,
    [int]$MeasuredIterations = 5,
    [int]$SettleMilliseconds = 1400,
    [string[]]$ScenarioNames = @()
)

$ErrorActionPreference = "Stop"

$validation = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $validation)
$exe = Join-Path $root "build\output\PDFLigero.exe"
$run = Join-Path `
    $validation `
    ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" +
     ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
$captureDirectory = Join-Path $run "captures"
$recoveryDirectory = Join-Path $run "recovery-clean"
$csvPath = Join-Path $run "benchmark-results.csv"
$reportPath = Join-Path $run "qa-report.txt"

New-Item -ItemType Directory -Force -Path $validation | Out-Null
New-Item -ItemType Directory -Force -Path $run | Out-Null
New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $recoveryDirectory | Out-Null

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Falta el ejecutable de producción: $exe"
}

$alreadyRunning = Get-Process PDFLigero,FirmaAutomatica -ErrorAction SilentlyContinue
if ($alreadyRunning) {
    throw "Cierra PDF Ligero antes de ejecutar el benchmark."
}

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PdfLigeroPerformanceNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(
        IntPtr hWnd,
        IntPtr deviceContext,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RedrawWindow(
        IntPtr hWnd,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
"@

$fixtures = [ordered]@{
    Vector = Join-Path `
        $root `
        "build\validation-plan-comparison-engine\run\plano-base-vectorial.pdf"
    ManyPages = Join-Path `
        $root `
        "build\insert-service-tests\run\long-81-pages.pdf"
    Mixed = Join-Path `
        $root `
        "build\validation-ocr-ui\run\integracion OCR - Málaga.pdf"
    LargeScan = Join-Path `
        $root `
        "build\validation-phase1-large\fixture-scanned-a4-32mb.pdf"
}

foreach ($fixture in $fixtures.Values) {
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw "Falta el fixture: $fixture"
    }
}

$fixtureHashes = @{}
foreach ($entry in $fixtures.GetEnumerator()) {
    $fixtureHashes[$entry.Key] =
        (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Value).Hash
}

$scenarios = @(
    [pscustomobject]@{
        Name = "EMPTY"
        Arguments = @()
        ExpectedFile = $null
        ThresholdReadyMs = 750
        ThresholdPrivateMiB = 100
    },
    [pscustomobject]@{
        Name = "VECTOR_2P"
        Arguments = @("--open", $fixtures.Vector)
        ExpectedFile = [IO.Path]::GetFileName($fixtures.Vector)
        ThresholdReadyMs = 1000
        ThresholdPrivateMiB = 150
    },
    [pscustomobject]@{
        Name = "VECTOR_81P"
        Arguments = @("--open", $fixtures.ManyPages)
        ExpectedFile = [IO.Path]::GetFileName($fixtures.ManyPages)
        ThresholdReadyMs = 1750
        ThresholdPrivateMiB = 175
    },
    [pscustomobject]@{
        Name = "SCAN_16P_33MIB"
        Arguments = @("--open", $fixtures.LargeScan)
        ExpectedFile = [IO.Path]::GetFileName($fixtures.LargeScan)
        ThresholdReadyMs = 2500
        ThresholdPrivateMiB = 250
    },
    [pscustomobject]@{
        Name = "MULTITAB_4_LAZY"
        Arguments = @(
            "--open",
            $fixtures.Vector,
            $fixtures.ManyPages,
            $fixtures.Mixed,
            $fixtures.LargeScan)
        ExpectedFile = [IO.Path]::GetFileName($fixtures.Vector)
        ThresholdReadyMs = 1500
        ThresholdPrivateMiB = 175
    }
)

if ($ScenarioNames.Count -gt 0) {
    $requestedNames = @($ScenarioNames | ForEach-Object {
        $_.Trim().ToUpperInvariant()
    })
    $scenarios = @($scenarios | Where-Object {
        $requestedNames -contains $_.Name
    })
    if ($scenarios.Count -eq 0) {
        throw "Ningún escenario solicitado es válido."
    }
}

function Convert-ToCommandLineArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Update-ProcessPeaks {
    param(
        [Diagnostics.Process]$Process,
        [ref]$PeakWorkingSet,
        [ref]$PeakPrivate
    )

    try {
        $Process.Refresh()
        if ($Process.HasExited) {
            return
        }

        $PeakWorkingSet.Value = [Math]::Max(
            [long]$PeakWorkingSet.Value,
            [long]$Process.WorkingSet64)
        $PeakPrivate.Value = [Math]::Max(
            [long]$PeakPrivate.Value,
            [long]$Process.PrivateMemorySize64)
    }
    catch {
    }
}

function Test-WindowResponsive {
    param(
        [IntPtr]$Handle,
        [Collections.Generic.List[double]]$PingTimes,
        [ref]$TimeoutCount
    )

    if ($Handle -eq [IntPtr]::Zero) {
        return $false
    }

    $result = [IntPtr]::Zero
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $callResult = [PdfLigeroPerformanceNative]::SendMessageTimeout(
        $Handle,
        0,
        [IntPtr]::Zero,
        [IntPtr]::Zero,
        2,
        500,
        [ref]$result)
    $timer.Stop()
    $PingTimes.Add($timer.Elapsed.TotalMilliseconds)
    if ($callResult -eq [IntPtr]::Zero) {
        $TimeoutCount.Value++
        return $false
    }

    return $true
}

function Save-WindowCapture {
    param(
        [IntPtr]$Handle,
        [string]$Path
    )

    $rect = New-Object PdfLigeroPerformanceNative+RECT
    if (-not [PdfLigeroPerformanceNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw "No se pudo leer el rectángulo de la ventana."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "La ventana no tiene un tamaño capturable."
    }

    $bitmap = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        try {
            $printed = [PdfLigeroPerformanceNative]::PrintWindow(
                $Handle,
                $deviceContext,
                2)
        }
        finally {
            $graphics.ReleaseHdc($deviceContext)
        }

        if (-not $printed) {
            [PdfLigeroPerformanceNative]::SetForegroundWindow($Handle) |
                Out-Null
            Start-Sleep -Milliseconds 120
            $graphics.CopyFromScreen(
                $rect.Left,
                $rect.Top,
                0,
                0,
                (New-Object Drawing.Size $width, $height))
        }

        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return 0D
    }

    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling($Percentile * $ordered.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, $index))
    return [double]$ordered[$index]
}

function Invoke-ScenarioRun {
    param(
        [pscustomobject]$Scenario,
        [int]$Iteration,
        [bool]$Warmup,
        [bool]$Capture
    )

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exe
    $startInfo.WorkingDirectory = Split-Path -Parent $exe
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ($Scenario.Arguments |
        ForEach-Object { Convert-ToCommandLineArgument $_ }) -join ' '
    $startInfo.EnvironmentVariables["PDFLIGERO_RECOVERY_ROOT"] =
        $recoveryDirectory

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    $windowMilliseconds = $null
    $loadedMilliseconds = $null
    $readyMilliseconds = $null
    $peakWorkingSet = [long]0
    $peakPrivate = [long]0
    $pingTimes = New-Object 'Collections.Generic.List[double]'
    $timeoutCount = 0
    $forcedKill = $false
    $finalWorkingSet = [long]0
    $finalPrivate = [long]0
    $finalHandles = 0
    $title = ""
    $closeMainWindowSent = $false
    $closeRetrySent = $false
    $closeMilliseconds = 0D
    $closeWindowGoneMilliseconds = 0D

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                throw "El proceso terminó antes de mostrar el documento."
            }

            Update-ProcessPeaks `
                -Process $process `
                -PeakWorkingSet ([ref]$peakWorkingSet) `
                -PeakPrivate ([ref]$peakPrivate)

            $process.Refresh()
            $handle = $process.MainWindowHandle
            if ($handle -ne [IntPtr]::Zero) {
                if ($null -eq $windowMilliseconds) {
                    $windowMilliseconds = $timer.Elapsed.TotalMilliseconds
                }

                Test-WindowResponsive `
                    -Handle $handle `
                    -PingTimes $pingTimes `
                    -TimeoutCount ([ref]$timeoutCount) | Out-Null

                $title = $process.MainWindowTitle
                $isLoaded = $null -eq $Scenario.ExpectedFile -or
                    $title.IndexOf(
                        $Scenario.ExpectedFile,
                        [StringComparison]::OrdinalIgnoreCase) -ge 0
                if ($isLoaded) {
                    $loadedMilliseconds = $timer.Elapsed.TotalMilliseconds
                    [PdfLigeroPerformanceNative]::RedrawWindow(
                        $handle,
                        [IntPtr]::Zero,
                        [IntPtr]::Zero,
                        0x0185) | Out-Null
                    if (Test-WindowResponsive `
                        -Handle $handle `
                        -PingTimes $pingTimes `
                        -TimeoutCount ([ref]$timeoutCount)) {
                        $readyMilliseconds = $timer.Elapsed.TotalMilliseconds
                        break
                    }
                }
            }

            Start-Sleep -Milliseconds 25
        }

        if ($null -eq $readyMilliseconds) {
            throw "La ventana/documento no quedó listo en 15 segundos. Título='$title'."
        }

        $settleDeadline = [DateTime]::UtcNow.AddMilliseconds(
            $SettleMilliseconds)
        while ([DateTime]::UtcNow -lt $settleDeadline) {
            Update-ProcessPeaks `
                -Process $process `
                -PeakWorkingSet ([ref]$peakWorkingSet) `
                -PeakPrivate ([ref]$peakPrivate)
            Test-WindowResponsive `
                -Handle $process.MainWindowHandle `
                -PingTimes $pingTimes `
                -TimeoutCount ([ref]$timeoutCount) | Out-Null
            Start-Sleep -Milliseconds 45
        }

        $process.Refresh()
        $finalWorkingSet = $process.WorkingSet64
        $finalPrivate = $process.PrivateMemorySize64
        $finalHandles = $process.HandleCount
        $title = $process.MainWindowTitle

        if ($Capture) {
            Save-WindowCapture `
                -Handle $process.MainWindowHandle `
                -Path (Join-Path `
                    $captureDirectory `
                    ($Scenario.Name.ToLowerInvariant() + ".png"))
        }
    }
    finally {
        $closeTimer = [Diagnostics.Stopwatch]::StartNew()
        if (-not $process.HasExited) {
            $closeHandle = $process.MainWindowHandle
            if ($closeHandle -ne [IntPtr]::Zero) {
                $closeMainWindowSent =
                    [PdfLigeroPerformanceNative]::PostMessage(
                        $closeHandle,
                        0x0010,
                        [IntPtr]::Zero,
                        [IntPtr]::Zero)
            }

            $closeDeadline = [DateTime]::UtcNow.AddSeconds(5)
            $windowGoneRecorded = $false
            while (-not $process.HasExited -and
                [DateTime]::UtcNow -lt $closeDeadline) {
                $process.Refresh()
                if (-not $windowGoneRecorded -and
                    $process.MainWindowHandle -eq [IntPtr]::Zero) {
                    $closeWindowGoneMilliseconds =
                        $closeTimer.Elapsed.TotalMilliseconds
                    $windowGoneRecorded = $true
                }

                Start-Sleep -Milliseconds 20
            }

            if (-not $process.HasExited) {
                $closeRetrySent = $process.CloseMainWindow()
                if (-not $process.WaitForExit(3000)) {
                    $forcedKill = $true
                    $process.Kill()
                    $process.WaitForExit(3000) | Out-Null
                }
            }

            if (-not $windowGoneRecorded) {
                $closeWindowGoneMilliseconds =
                    $closeTimer.Elapsed.TotalMilliseconds
            }
        }

        $closeTimer.Stop()
        $closeMilliseconds = $closeTimer.Elapsed.TotalMilliseconds
        $timer.Stop()
        $process.Dispose()
        Start-Sleep -Milliseconds 180
    }

    return [pscustomobject]@{
        Scenario = $Scenario.Name
        Iteration = $Iteration
        Warmup = $Warmup
        WindowMs = [Math]::Round([double]$windowMilliseconds, 1)
        LoadedMs = [Math]::Round([double]$loadedMilliseconds, 1)
        ReadyPaintMs = [Math]::Round([double]$readyMilliseconds, 1)
        WorkingSetMiB = [Math]::Round($finalWorkingSet / 1MB, 1)
        PrivateMiB = [Math]::Round($finalPrivate / 1MB, 1)
        PeakWorkingSetMiB = [Math]::Round($peakWorkingSet / 1MB, 1)
        PeakPrivateMiB = [Math]::Round($peakPrivate / 1MB, 1)
        Handles = $finalHandles
        PingP95Ms = [Math]::Round(
            (Get-Percentile -Values $pingTimes.ToArray() -Percentile 0.95),
            1)
        PingMaxMs = [Math]::Round(
            (($pingTimes.ToArray() | Measure-Object -Maximum).Maximum),
            1)
        PingTimeouts500Ms = $timeoutCount
        CloseRequestSent = $closeMainWindowSent
        CloseRetrySent = $closeRetrySent
        CloseWindowGoneMs = [Math]::Round(
            $closeWindowGoneMilliseconds,
            1)
        CloseMs = [Math]::Round($closeMilliseconds, 1)
        ForcedKill = $forcedKill
        WindowTitle = $title
    }
}

$results = New-Object 'Collections.Generic.List[object]'
$totalRounds = $WarmupIterations + $MeasuredIterations
for ($round = 0; $round -lt $totalRounds; $round++) {
    for ($offset = 0; $offset -lt $scenarios.Count; $offset++) {
        $scenario = $scenarios[($offset + $round) % $scenarios.Count]
        $isWarmup = $round -lt $WarmupIterations
        $iteration = if ($isWarmup) {
            $round + 1
        }
        else {
            $round - $WarmupIterations + 1
        }
        $capture = -not $isWarmup -and
            $iteration -eq $MeasuredIterations
        Write-Host (
            "RUN scenario={0} iteration={1} warmup={2}" -f
            $scenario.Name,
            $iteration,
            $isWarmup)
        $results.Add((Invoke-ScenarioRun `
            -Scenario $scenario `
            -Iteration $iteration `
            -Warmup $isWarmup `
            -Capture $capture))
    }
}

$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$lines = New-Object 'Collections.Generic.List[string]'
$lines.Add("PDF LIGERO - BENCHMARK DE ARRANQUE Y LIGEREZA")
$lines.Add("FechaUTC=" + [DateTime]::UtcNow.ToString("o"))
$lines.Add("Exe=" + $exe)
$lines.Add("ExeSHA256=" + (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash)
$lines.Add("WarmupPorEscenario=" + $WarmupIterations)
$lines.Add("IteracionesMedidasPorEscenario=" + $MeasuredIterations)
$lines.Add("SettleMilliseconds=" + $SettleMilliseconds)
$lines.Add("NotaCache=Mediciones calientes tras calentamiento; no se vació la caché de Windows.")

$allPass = $true
foreach ($scenario in $scenarios) {
    $measured = @($results | Where-Object {
        $_.Scenario -eq $scenario.Name -and -not $_.Warmup
    })
    $readyMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.ReadyPaintMs }) `
        -Percentile 0.5
    $readyP95 = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.ReadyPaintMs }) `
        -Percentile 0.95
    $windowMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.WindowMs }) `
        -Percentile 0.5
    $privateMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.PrivateMiB }) `
        -Percentile 0.5
    $workingSetMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.WorkingSetMiB }) `
        -Percentile 0.5
    $privatePeak = ($measured | Measure-Object PeakPrivateMiB -Maximum).Maximum
    $pingP95 = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.PingP95Ms }) `
        -Percentile 0.95
    $pingMax = ($measured | Measure-Object PingMaxMs -Maximum).Maximum
    $timeouts = ($measured | Measure-Object PingTimeouts500Ms -Sum).Sum
    $forced = @($measured | Where-Object { $_.ForcedKill }).Count
    $closeMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.CloseMs }) `
        -Percentile 0.5
    $closeMax = ($measured | Measure-Object CloseMs -Maximum).Maximum
    $closeWindowMedian = Get-Percentile `
        -Values @($measured | ForEach-Object { $_.CloseWindowGoneMs }) `
        -Percentile 0.5
    $scenarioPass =
        $readyMedian -le $scenario.ThresholdReadyMs -and
        $privateMedian -le $scenario.ThresholdPrivateMiB -and
        $timeouts -eq 0 -and
        $forced -eq 0
    $allPass = $allPass -and $scenarioPass

    $scenarioResult = if ($scenarioPass) { "PASS" } else { "FAIL" }
    $summaryLine = (
        "{0}: WindowMedian={1:N1}ms; ReadyPaintMedian={2:N1}ms; " +
        "ReadyPaintP95={3:N1}ms; WorkingSetMedian={4:N1}MiB; " +
        "PrivateMedian={5:N1}MiB; PeakPrivateMax={6:N1}MiB; " +
        "PingP95={7:N1}ms; PingMax={8:N1}ms; Timeouts500ms={9}; " +
        "CloseWindowMedian={10:N1}ms; CloseProcessMedian={11:N1}ms; " +
        "CloseProcessMax={12:N1}ms; ForcedKills={13}; RESULTADO={14}") -f
        $scenario.Name,
        $windowMedian,
        $readyMedian,
        $readyP95,
        $workingSetMedian,
        $privateMedian,
        $privatePeak,
        $pingP95,
        $pingMax,
        $timeouts,
        $closeWindowMedian,
        $closeMedian,
        $closeMax,
        $forced,
        $scenarioResult
    $lines.Add($summaryLine)
}

$hashesUnchanged = $true
foreach ($entry in $fixtures.GetEnumerator()) {
    $after = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Value).Hash
    if ($after -ne $fixtureHashes[$entry.Key]) {
        $hashesUnchanged = $false
    }
}
$lines.Add("FIXTURES_SHA256_INTACTOS=" + $(if ($hashesUnchanged) { "PASS" } else { "FAIL" }))

$remaining = @(Get-Process PDFLigero,FirmaAutomatica -ErrorAction SilentlyContinue)
$lines.Add("PROCESOS_RESIDUALES=" + $remaining.Count)
if (-not $hashesUnchanged -or $remaining.Count -ne 0) {
    $allPass = $false
}

$lines.Add("RESULTADO_GLOBAL=" + $(if ($allPass) { "PASS" } else { "FAIL" }))
$lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
Set-Content `
    -LiteralPath (Join-Path $validation "latest-run.txt") `
    -Value $run `
    -Encoding UTF8

$lines | ForEach-Object { Write-Host $_ }
Write-Host "CSV=$csvPath"
Write-Host "REPORT=$reportPath"

if (-not $allPass) {
    exit 1
}
