# F1 audit: verify all async void event handlers have try/catch coverage.
# Exit 0 = all covered; exit 1 = list uncovered handlers.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'src/PChabit.App'

$results = New-Object System.Collections.Generic.List[object]
Get-ChildItem $app -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object {
    $file = $_.FullName
    $lines = Get-Content $file
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*//') { continue }   # skip pure comment lines
        if ($lines[$i] -match '\basync\s+void\s+(\w+)') {
            $name = $Matches[1]
            # brace matching from the signature line
            $depth = 0; $started = $false; $body = New-Object System.Collections.Generic.List[string]
            for ($j = $i; $j -lt $lines.Count; $j++) {
                foreach ($ch in $lines[$j].ToCharArray()) {
                    if ($ch -eq '{') { $depth++; $started = $true }
                    elseif ($ch -eq '}') { $depth-- }
                }
                $body.Add($lines[$j])
                if ($started -and $depth -eq 0) { break }
            }
            $bodyText = $body -join "`n"
            $hasTry = $bodyText -match '\btry\b'
            $results.Add([pscustomobject]@{
                File   = $file.Replace($app, '').TrimStart('\')
                Line   = $i + 1
                Method = $name
                TryCatch = $hasTry
            })
        }
    }
}

$uncovered = $results | Where-Object { -not $_.TryCatch }
$total = $results.Count
$covered = $total - $uncovered.Count
Write-Host "async void methods: $total, with try/catch: $covered, uncovered: $($uncovered.Count)"
if ($uncovered) {
    $uncovered | Format-Table -AutoSize | Out-String | Write-Host
    exit 1
}
exit 0
