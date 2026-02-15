Get-ChildItem "C:\Users\idlee\.nuget\packages" -Filter "*bak*" -Directory | ForEach-Object {
    Write-Host "Processing: $($_.Name)"
    takeown /f $_.FullName /r /d y 2>&1 | Out-Null
    Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue
}
Get-ChildItem "C:\Users\idlee\.nuget\packages" -Filter "microsoft.windowsappsdk*" | Select-Object -ExpandProperty Name
