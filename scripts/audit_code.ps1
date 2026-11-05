#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
PChabit code baseline audit (stage A4 of the engineering refactor plan).

Produces a repeatable JSON + Markdown snapshot of the codebase baseline metrics:
  1. count and distribution of empty catch blocks
  2. count of `catch (Exception ...)` and top files
  3. count of `async void` and `Task.Run`
  4. count of `using PChabit.Infrastructure` inside App/ViewModel code
  5. per-file line count Top 20

Usage:
    ./scripts/audit_code.ps1 [-OutputDir <dir>]
      OutputDir defaults to the project root.

Exit code: 0 on success, non-zero on failure.
#>

[CmdletBinding()]
param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = $ProjectRoot
}

# ---------- helpers ----------
function Write-Metrics {
    param(
        [pscustomobject]$Data,
        [string]$OutJson,
        [string]$OutMd
    )
    $Data | ConvertTo-Json -Depth 6 | Set-Content -Path $OutJson -Encoding utf8
    $md = @()
    $md += "# PChabit code baseline audit"
    $md += ""
    $md += "- Generated: $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))"
    $md += "- Empty catch count: $($Data.emptyCatchCount)"
    $md += "- catch(Exception) count: $($Data.catchGenericCount)"
    $md += "- async void count: $($Data.asyncVoidCount)"
    $md += "- Task.Run count: $($Data.taskRunCount)"
    $md += "- using Infrastructure in App/ViewModel: $($Data.usingInfrastructureCount)"
    $md += ""
    $md += "## Empty catch by file"
    $md += ""
    $md += "| File | Count |"
    $md += "|---|---|"
    foreach ($f in $Data.emptyCatchByFile) {
        $md += "| $($f.file) | $($f.count) |"
    }
    $md += ""
    $md += "## catch(Exception) top files"
    $md += ""
    $md += "| File | Count |"
    $md += "|---|---|"
    foreach ($f in $Data.catchGenericTopFiles) {
        $md += "| $($f.file) | $($f.count) |"
    }
    $md += ""
    $md += "## Top 20 files by line count"
    $md += ""
    $md += "| File | Lines |"
    $md += "|---|---|"
    foreach ($f in $Data.topFilesByLines) {
        $md += "| $($f.file) | $($f.lines) |"
    }
    $md += ""
    $md | Set-Content -Path $OutMd -Encoding utf8
}

# ---------- 1 & 2: catch scanning ----------
$csFiles = Get-ChildItem -Path (Join-Path $ProjectRoot "src") -Recurse -File -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$emptyCatchByFile = @{}
$catchGenericByFile = @{}
$asyncVoidCount = 0
$taskRunCount = 0
$usingInfraAppVm = 0

foreach ($f in $csFiles) {
    $lines = Get-Content -Path $f.FullName
    $rel = $f.FullName.Substring($ProjectRoot.Length + 1)

    # empty catch: `catch ... { }` with nothing meaningful inside
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        # match "catch" possibly with "(Exception e)" then "{" and close "}" with no body
        if ($line -match '^catch\s*(\([^)]*\))?\s*\{\s*\}$') {
            $emptyCatchByFile[$rel] = ($emptyCatchByFile[$rel] ?? 0) + 1
        }
        elseif ($line -match '^catch\s*\{') {
            # catch { } spanning lines: check next line closes immediately
            if ($i + 1 -lt $lines.Count -and $lines[$i + 1].Trim() -eq '}') {
                $emptyCatchByFile[$rel] = ($emptyCatchByFile[$rel] ?? 0) + 1
            }
        }
        # notably generic catch
        if ($line -match '^catch\s*\(Exception\s*\w*\s*\)') {
            $catchGenericByFile[$rel] = ($catchGenericByFile[$rel] ?? 0) + 1
        }
        # async void
        $asyncVoidCount += ([regex]::Matches($line, 'async\s+void\b')).Count
        # Task.Run
        $taskRunCount += ([regex]::Matches($line, 'Task\.Run\b')).Count
    }

    # usings in App + ViewModels
    $isAppOrVm = $rel -match 'PChabit\.App[\\/]' -or $rel -match 'ViewModels?[\\/]'
    if ($isAppOrVm) {
        foreach ($line in $lines) {
            if ($line -match '^\s*using\s+PChabit\.Infrastructure(?:\.[A-Za-z]+)?\s*;') {
                $usingInfraAppVm++
            }
        }
    }
}

$emptyCatchList = $emptyCatchByFile.GetEnumerator() |
    Sort-Object { $_.Value } -Descending |
    ForEach-Object { [pscustomobject]@{ file = $_.Key; count = $_.Value } }

$catchGenericTop = $catchGenericByFile.GetEnumerator() |
    Sort-Object { $_.Value } -Descending |
    Select-Object -First 10 |
    ForEach-Object { [pscustomobject]@{ file = $_.Key; count = $_.Value } }

$catchGenericTotal = ($catchGenericByFile.Values | Measure-Object -Sum).Sum

# ---------- 5: top files by line count ----------
$topFiles = Get-ChildItem -Path (Join-Path $ProjectRoot "src") -Recurse -File -Include "*.cs", "*.xaml" |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object {
        [pscustomobject]@{
            file  = $_.FullName.Substring($ProjectRoot.Length + 1)
            lines = (Get-Content -Path $_.FullName).Count
        }
    } |
    Sort-Object -Property lines -Descending |
    Select-Object -First 20

$data = [pscustomobject]@{
    emptyCatchCount          = ($emptyCatchList | Measure-Object -Property count -Sum).Sum
    emptyCatchByFile         = $emptyCatchList
    catchGenericCount        = $catchGenericTotal
    catchGenericTopFiles     = $catchGenericTop
    asyncVoidCount           = $asyncVoidCount
    taskRunCount             = $taskRunCount
    usingInfrastructureCount = $usingInfraAppVm
    topFilesByLines          = $topFiles
}

$outJson = Join-Path $OutputDir "audit_baseline.json"
$outMd   = Join-Path $OutputDir "audit_baseline.md"
Write-Metrics -Data $data -OutJson $outJson -OutMd $outMd

Write-Host "Audit complete."
Write-Host "  empty catch      : $($data.emptyCatchCount)"
Write-Host "  catch(Exception) : $($data.catchGenericCount) (top 10 files shown)"
Write-Host "  async void       : $($data.asyncVoidCount)"
Write-Host "  Task.Run         : $($data.taskRunCount)"
Write-Host "  using Infra(VM)  : $($data.usingInfrastructureCount)"
Write-Host "  output           : $outJson"
Write-Output $data
exit 0