# Force unlock and delete NuGet packages
$folders = @(
    "C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk.winui",
    "C:\Users\idlee\.nuget\packages\microsoft.bcl.asyncinterfaces"
)

foreach ($folder in $folders) {
    if (Test-Path $folder) {
        Write-Host "Processing: $folder"
        
        # Take ownership
        & takeown /f $folder /r /d y 2>&1 | Out-Null
        
        # Grant full control
        & icacls $folder /grant "$(whoami):F" /t 2>&1 | Out-Null
        
        # Delete
        Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
        
        Write-Host "Deleted: $(!(Test-Path $folder))"
    }
}

# Also check for locked files
Get-Process | Where-Object { 
    $_.MainModule.FileName -like "*nuget*" -or 
    $_.MainModule.FileName -like "*dotnet*" 
} | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Done"
