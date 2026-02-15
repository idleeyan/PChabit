@echo off
cd /d "C:\Users\idlee\.nuget\packages"
for /d %%i in (microsoft.windowsappsdk.*bak*) do (
    takeown /f "%%i" /r /d y >nul 2>&1
    rd /s /q "%%i" 2>nul
)
echo Done cleaning backup folders
