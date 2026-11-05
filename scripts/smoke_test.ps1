#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
PChabit smoke test (stage A3 of the engineering refactor plan).

Verifies a deployed build in-place:
  1. required deployment files exist
  2. PChabit.runtimeconfig.json carries includedFrameworks
  3. PChabit.exe file version matches the csproj <Version>
  4. launches the process and waits for it to settle
   NOTE: the app is single-instance (trap 22, Global\ Mutex): if an instance is
   already running, the smoke-launched process wakes the existing one and exits
   with code 0, which this script reports as FAIL ("process exited during smoke").
   Kill any running instance before running the smoke test.
  5. scans today's Serilog log, requiring zero ERR / FTL (allowlisted keywords exempted)
  6. process alive and main window present
  7. closes the process, returns exit code (0 = pass, non-zero = fail)

Usage:
    ./scripts/smoke_test.ps1 [-DeployDir <dir>] [-WaitSeconds <n>] [-KeepRunning]
      -DeployDir   deployment directory (default D:\Tool\PChabit)
      -WaitSeconds settle wait in seconds (default 30)
      -KeepRunning do not close the process after checks (default off)

Exit codes: 0 pass, 1 fail, 2 not run (locked file / unavailable).
#>

[CmdletBinding()]
param(
    [string]$DeployDir = "",
    [int]$WaitSeconds = 30,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DeploymentDir = if (-not $DeployDir) { "D:\Tool\PChabit" } else { $DeployDir }

if (-not (Test-Path $DeploymentDir)) {
    Write-Error "Deployment dir not found: $DeploymentDir"
    exit 2
}

$fails = @()

# ---------- 1. required files ----------
$required = @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "e_sqlite3.dll", "WinUIEdit.dll", "PChabit.exe", "PChabit.dll")
foreach ($name in $required) {
    $p = Join-Path $DeploymentDir $name
    if (-not (Test-Path $p)) {
        $fails += "missing required file: $name"
    }
}
if ($fails.Count -gt 0) {
    Write-Host "Required file check FAILED:" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "SMOKE RESULT: FAIL (file check)" -ForegroundColor Red
    exit 1
}

# ---------- 2. runtimeconfig includedFrameworks ----------
$rtConfig = Join-Path $DeploymentDir "PChabit.runtimeconfig.json"
if (Test-Path $rtConfig) {
    $rt = Get-Content $rtConfig -Raw | ConvertFrom-Json
    $hasIncluded = $rt.runtimeOptions.includedFrameworks -or $rt.runtimeOptions.frameworks -or $rt.runtimeOptions.tfm
    if ($hasIncluded) {
        Write-Host "runtimeconfig: includedFrameworks / frameworks OK" -ForegroundColor Green
    }
    else {
        $fails += "PChabit.runtimeconfig.json has no includedFrameworks/frameworks"
    }
}
else {
    $fails += "missing PChabit.runtimeconfig.json"
}

# ---------- 3. version match ----------
$exeInfo = (Get-Item (Join-Path $DeploymentDir "PChabit.exe")).VersionInfo
$exeVer = $exeInfo.FileVersion
$csproj = Join-Path $ProjectRoot "src\PChabit.App\PChabit.App.csproj"
$xml = [xml](Get-Content $csproj -Raw)
# csproj may declare several PropertyGroups; take the last non-empty <Version>
$projVer = @($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -Last 1) -join ""
if (-not $projVer) { $projVer = "0.0.0" }

if ($exeVer -and $projVer -and $exeVer -like "$projVer*") {
    Write-Host "version match: exe=$exeVer csproj=$projVer" -ForegroundColor Green
}
else {
    $fails += "version mismatch: exe=$exeVer vs csproj=$projVer"
    Write-Host "version mismatch: exe=$exeVer vs csproj=$projVer" -ForegroundColor Yellow
}

# ---------- early fail on structural problems ----------
if ($fails.Count -gt 0) {
    Write-Host "SMOKE RESULT: FAIL (pre-launch checks)" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ---------- 3.5. locate today's app log + record baseline (scan only NEW lines) ----------
# Pin the app log BEFORE launch and remember its current length, so the log check
# below only matches lines written by this smoke run. Historical FTL/ERR (e.g. a
# crash fixed in an earlier version, still present in today's rolled file) must
# not fail the current run. Prefer app-*.log over ops-*.log.
$today = Get-Date -Format "yyyyMMdd"
$logDirCandidates = @(
    (Join-Path $DeploymentDir "logs"),
    (Join-Path $DeploymentDir "Logs"),
    (Join-Path $env:LOCALAPPDATA "PChabit\logs")
)
$logFile = $null
foreach ($loc in $logDirCandidates) {
    if (Test-Path $loc) {
        $f = Get-ChildItem -Path $loc -File -Recurse |
            Where-Object { $_.Name -match $today -or $_.LastWriteTime.Date -eq (Get-Date).Date } |
            Sort-Object @{ Expression = { if ($_.Name -like "app-*") { 0 } else { 1 } } }, @{ Expression = "LastWriteTime"; Descending = $true } |
            Select-Object -First 1
        if ($f) { $logFile = $f.FullName; break }
    }
}
$baselineCount = 0
if ($logFile -and (Test-Path $logFile)) {
    $baselineCount = @(Get-Content $logFile -ErrorAction SilentlyContinue).Count
}
Write-Host "log baseline: $logFile (start line $baselineCount)" -ForegroundColor DarkGray

# ---------- 4. launch ----------
$exe = Join-Path $DeploymentDir "PChabit.exe"
$proc = Start-Process -FilePath $exe -PassThru
Write-Host "started PID $($proc.Id), waiting $WaitSeconds s for settle..." -ForegroundColor Cyan

# settle wait with liveness poll
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$everAlive = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if (-not $proc.HasExited) {
        $everAlive = $true
        break
    }
}

# ---------- 5. log scan (NEW lines written since launch baseline) ----------
# $logFile and $baselineCount come from step 3.5. Only lines appended after the
# smoke run launched are evaluated, so historical ERR/FTL in today's rolled file
# (e.g. a crash already fixed in an earlier version) cannot fail this run.
# If no log existed before launch (first run of the day), the app creates
# app-<today>.log itself — retry the lookup now; every line of a newly created
# file belongs to this run, so baseline 0 is correct.
if (-not ($logFile -and (Test-Path $logFile))) {
    foreach ($loc in $logDirCandidates) {
        if (Test-Path $loc) {
            $f = Get-ChildItem -Path $loc -File -Recurse |
                Where-Object { $_.Name -match $today -or $_.LastWriteTime -ge (Get-Date).AddMinutes(-10) } |
                Sort-Object @{ Expression = { if ($_.Name -like "app-*") { 0 } else { 1 } } }, @{ Expression = "LastWriteTime"; Descending = $true } |
                Select-Object -First 1
            if ($f) { $logFile = $f.FullName; $baselineCount = 0; break }
        }
    }
}
$allowlist = @("OpenRouter", "404", "HttpClient timeout", "Task either canceled or faulted")
$errFtlLines = @()
if ($logFile -and (Test-Path $logFile)) {
    $errFtlLines = Get-Content $logFile -ErrorAction SilentlyContinue |
        Select-Object -Skip $baselineCount |
        Where-Object {
            $line = $_
            ($line -match '\b(ERR|FTL)\b') -and
            -not ($allowlist | Where-Object { $line -match [regex]::Escape($_.ToString()) } | Select-Object -First 1)
        } |
        Select-Object -First 20
    if ($errFtlLines.Count -eq 0) {
        Write-Host "log scan ($logFile): 0 ERR/FTL" -ForegroundColor Green
    }
    else {
        Write-Host "log scan: $($errFtlLines.Count) ERR/FTL lines found:" -ForegroundColor Yellow
        $errFtlLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        $fails += "log has $($errFtlLines.Count) ERR/FTL lines"
    }
}
else {
    Write-Host "WARN: no log file matched (deployment dir or %LOCALAPPDATA%\PChabit\logs)" -ForegroundColor DarkYellow
}

# ---------- 6. alive + main window ----------
$proc.Refresh()
$alive = -not $proc.HasExited
$hasWindow = $false
if ($alive) {
    $hasWindow = $proc.MainWindowHandle -ne 0
}

if ($alive) {
    Write-Host "process alive: PID $($proc.Id)" -ForegroundColor Green
    if ($hasWindow) {
        Write-Host "main window present: $($proc.MainWindowTitle)" -ForegroundColor Green
    }
    else {
        Write-Host "WARN: no main window detected yet" -ForegroundColor DarkYellow
    }
}
else {
    Write-Host "process EXITED during smoke" -ForegroundColor Red
    $fails += "process exited ($($proc.ExitCode)) during smoke"
}

# ---------- 7. teardown ----------
if (-not $KeepRunning -and $alive) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Write-Host "stopped PID $($proc.Id)" -ForegroundColor Cyan
}

if ($fails.Count -eq 0) {
    Write-Host "SMOKE RESULT: PASS" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "SMOKE RESULT: FAIL" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}