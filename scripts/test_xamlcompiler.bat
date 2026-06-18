@echo off
cd /d "E:\SYNC\My VS\PChabit\src\PChabit.App"
echo Working directory: %CD%
echo Running XamlCompiler.exe...
"C:\Users\idlee\.nuget\packages\microsoft.windowsappsdk.winui\2.2.1\buildTransitive\..\tools\net6.0\..\net472\XamlCompiler.exe" "obj\Release\net9.0-windows10.0.22621.0\win-x64\input.json" "obj\Release\net9.0-windows10.0.22621.0\win-x64\output.json"
echo Exit code: %ERRORLEVEL%
if exist "obj\Release\net9.0-windows10.0.22621.0\win-x64\output.json" (
    echo output.json EXISTS
) else (
    echo output.json NOT FOUND
)
