Add-Type -AssemblyName System.IO

function Delete-Recursive {
    param([string]$Path)
    
    if (!(Test-Path $Path)) { 
        Write-Host "$Path does not exist"
        return 
    }
    
    $files = Get-ChildItem $Path -File -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        try { 
            [System.IO.File]::Delete($file.FullName) 
        } catch { }
    }
    
    $dirs = Get-ChildItem $Path -Directory -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    foreach ($dir in $dirs) {
        try { 
            [System.IO.Directory]::Delete($dir.FullName, $true) 
        } catch { }
    }
    
    try { 
        [System.IO.Directory]::Delete($Path, $true) 
    } catch { }
    
    Write-Host "Deleted: $(Test-Path $Path)"
}

Delete-Recursive -Path "C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk.winui"
Delete-Recursive -Path "C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk"
