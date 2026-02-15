@echo off
setlocal EnableDelayedExpansion

echo ========================================
echo Tai-AI 发布脚本
echo ========================================
echo.

set CONFIGURATION=Release
set PLATFORM=x64
set VERSION_FILE=version.txt

if not exist %VERSION_FILE% (
    echo 1.0.0 > %VERSION_FILE%
)

set /p VERSION=<%VERSION_FILE%

echo 当前版本: %VERSION%
echo.

echo [1/4] 清理旧的发布文件...
if exist "publish" rmdir /s /q "publish"
mkdir "publish"

echo [2/4] 还原 NuGet 包...
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo 错误: NuGet 包还原失败
    exit /b 1
)

echo [3/4] 构建项目...
dotnet build src\Tai.App\Tai.App.csproj -c %CONFIGURATION% -p:Platform=%PLATFORM% --no-restore
if %ERRORLEVEL% neq 0 (
    echo 错误: 构建失败
    exit /b 1
)

echo [4/4] 发布应用...
dotnet publish src\Tai.App\Tai.App.csproj -c %CONFIGURATION% -p:Platform=%PLATFORM% --no-build -p:PublishProfile=win-x64
if %ERRORLEVEL% neq 0 (
    echo 错误: 发布失败
    exit /b 1
)

echo.
echo ========================================
echo 发布完成!
echo 输出目录: src\Tai.App\bin\%CONFIGURATION%\net8.0-windows10.0.22621.0\win-x64\publish
echo ========================================

echo.
echo 是否要增加版本号? (Y/N)
set /p INCREMENT=
if /i "%INCREMENT%"=="Y" (
    for /f "tokens=1,2,3 delims=." %%a in ("%VERSION%") do (
        set MAJOR=%%a
        set MINOR=%%b
        set PATCH=%%c
    )
    set /a PATCH+=1
    set NEW_VERSION=!MAJOR!.!MINOR!.!PATCH!
    echo !NEW_VERSION! > %VERSION_FILE%
    echo 版本已更新为: !NEW_VERSION!
)

echo.
pause
