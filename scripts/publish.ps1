#!/usr/bin/env pwsh
#Requires -Version 7.0

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64",
    
    [switch]$SkipTests,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([string]$Step, [string]$Message)
    Write-Host "[$Step] $Message" -ForegroundColor Yellow
}

if ($Help) {
    Write-Host @"
PChabit 发布脚本

用法: ./publish.ps1 [选项]

选项:
    -Configuration <Debug|Release>  构建配置 (默认: Release)
    -Platform <x64|x86|arm64>       目标平台 (默认: x64)
    -SkipTests                      跳过测试
    -Help                           显示帮助信息

示例:
    ./publish.ps1
    ./publish.ps1 -Configuration Debug -SkipTests
"@
    exit 0
}

Write-Header "PChabit 发布脚本"

Push-Location $ProjectRoot

try {
    Write-Step "1/6" "清理 obj/bin 和旧的发布文件..."
    $publishDir = Join-Path $ProjectRoot "publish"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir | Out-Null
    
    Get-ChildItem -Path $ProjectRoot -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $ProjectRoot -Recurse -Directory -Filter "bin" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Write-Step "2/6" "还原 NuGet 包..."
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet 包还原失败"
    }

    if (-not $SkipTests) {
        Write-Step "3/6" "运行测试..."
        dotnet test src/PChabit.Tests/PChabit.Tests.csproj -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "测试失败，是否继续发布? (Y/N)"
            $continue = Read-Host
            if ($continue -ne "Y") {
                throw "发布已取消"
            }
        }
    }

    Write-Step "4/6" "构建项目 (Release, x64)..."
    dotnet build src/PChabit.App/PChabit.App.csproj -c $Configuration -p:Platform=$Platform --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败"
    }

    Write-Step "5/6" "发布应用..."
    dotnet publish src/PChabit.App/PChabit.App.csproj `
        -c $Configuration `
        -p:Platform=$Platform `
        --no-build `
        -o $publishDir
    
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败"
    }

    Write-Step "6/6" "验证产出..."
    $dllPath = Join-Path $publishDir "PChabit.dll"
    $exePath = Join-Path $publishDir "PChabit.exe"

    if (Test-Path $dllPath) {
        $dllInfo = Get-Item $dllPath
        Write-Host "PChabit.dll  大小: $([math]::Round($dllInfo.Length / 1MB, 2)) MB  时间: $($dllInfo.LastWriteTime)" -ForegroundColor Green
    }
    else {
        throw "PChabit.dll 未生成!"
    }

    if (Test-Path $exePath) {
        $exeInfo = Get-Item $exePath
        Write-Host "PChabit.exe  大小: $([math]::Round($exeInfo.Length / 1MB, 2)) MB  时间: $($exeInfo.LastWriteTime)" -ForegroundColor Green
    }

    $totalSize = (Get-ChildItem -Path $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Header "发布完成!"
    Write-Host "输出目录: $publishDir" -ForegroundColor Green
    Write-Host "总大小: $([math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Gray

} catch {
    Write-Error "发布失败: $_"
    exit 1
} finally {
    Pop-Location
}
