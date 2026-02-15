# Script to delete stubborn folder
$source = "C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk.winui"
$target = "C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk.winui.old"

# Try to rename first
try {
    Rename-Item -Path $source -NewName "microsoft.windowsappsdk.winui.old" -Force -ErrorAction Stop
    Write-Host "Renamed successfully"
    
    # Now try to delete
    Start-Sleep -Seconds 1
    Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
    Write-Host "Deleted: $(Test-Path $source)"
}
catch {
    Write-Host "Error: $_"
    # Try to at least remove files inside
    Get-ChildItem $source -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem $source -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
