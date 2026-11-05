#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
Layering guard (stage E1 of the engineering refactor plan).

Enforces the App-layer boundary rule: ViewModels must not add NEW
`using PChabit.Infrastructure.*` imports. The current baseline is
whitelisted in scripts/layering-allowlist.txt; the goal (stage E3)
is to shrink that whitelist to zero as ViewModels migrate to
query services.

Checks:
  - Scans src/PChabit.App/ViewModels/**/*.cs for
    `using PChabit.Infrastructure.<ns>;` lines.
  - Compares the found set against the allowlist (format:
    `ViewModels/<File>.cs|PChabit.Infrastructure.<ns>`, one per line).
  - NEW violations -> exit code 1 (blocks CI).
  - Allowlisted entries that no longer exist -> info message
    suggesting to prune the allowlist (exit 0).

Usage:
    pwsh -File scripts/check_layering.ps1 [-ProjectRoot <dir>] [-AllowlistPath <file>]

Exit codes: 0 pass, 1 new violation(s), 2 not run (missing inputs).
#>

[CmdletBinding()]
param(
    [string]$ProjectRoot = "",
    [string]$AllowlistPath = ""
)

$ErrorActionPreference = "Stop"

$root = if ($ProjectRoot) { $ProjectRoot } else { Split-Path -Parent $PSScriptRoot }
$allowlistFile = if ($AllowlistPath) { $AllowlistPath } else { Join-Path $PSScriptRoot "layering-allowlist.txt" }

if (-not (Test-Path $root)) {
    Write-Error "Project root not found: $root"
    exit 2
}
if (-not (Test-Path $allowlistFile)) {
    Write-Error "Allowlist not found: $allowlistFile"
    exit 2
}

$appDir = Join-Path $root "src" | Join-Path -ChildPath "PChabit.App"
$vmDir = Join-Path $appDir "ViewModels"
if (-not (Test-Path $vmDir)) {
    Write-Error "ViewModels directory not found: $vmDir"
    exit 2
}

$pattern = '^\s*using\s+(PChabit\.Infrastructure(?:\.[A-Za-z0-9_.]+)?)\s*;'

$found = [System.Collections.Generic.SortedSet[string]]::new()
foreach ($file in (Get-ChildItem -Path $vmDir -Recurse -Filter *.cs)) {
    $rel = [IO.Path]::GetRelativePath($appDir, $file.FullName) -replace '\\', '/'
    foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding UTF8)) {
        if ($line -match $pattern) {
            $null = $found.Add("$rel|$($Matches[1])")
        }
    }
}

$allowlisted = [System.Collections.Generic.SortedSet[string]]::new()
foreach ($line in (Get-Content -LiteralPath $allowlistFile -Encoding UTF8)) {
    $t = $line.Trim()
    if ($t -and -not $t.StartsWith('#')) { $null = $allowlisted.Add($t) }
}

$violations = @($found | Where-Object { -not $allowlisted.Contains($_) })
$stale = @($allowlisted | Where-Object { -not $found.Contains($_) })

Write-Host ("Layering guard: {0} using(s) found, {1} allowlisted, {2} new violation(s), {3} stale allowlist entrie(s)" -f `
    $found.Count, $allowlisted.Count, $violations.Count, $stale.Count)

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "NEW layering violations (ViewModels importing PChabit.Infrastructure.*):" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  $v" -ForegroundColor Red }
    Write-Host ""
    Write-Host "ViewModels must depend on Application/Core abstractions, not Infrastructure."
    Write-Host "If this import is intentional for an already-planned migration, add it to"
    Write-Host "scripts/layering-allowlist.txt in the same commit."
    exit 1
}

if ($stale.Count -gt 0) {
    Write-Host ""
    Write-Host "Allowlist entries no longer present in code (prune to shrink the baseline):" -ForegroundColor Yellow
    foreach ($s in $stale) { Write-Host "  $s" -ForegroundColor Yellow }
}

exit 0
