#!/usr/bin/env pwsh
#Requires -Version 7.0

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64",
    
    [switch]$SkipTests,
    [switch]$IncrementVersion,
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

function Get-ProjectVersion {
    $versionFile = Join-Path $ProjectRoot "version.txt"
    if (Test-Path $versionFile) {
        return Get-Content $versionFile -Raw
    }
    return "1.0.0"
}

function Set-ProjectVersion {
    param([string]$Version)
    $versionFile = Join-Path $ProjectRoot "version.txt"
    $Version | Out-File $versionFile -NoNewline
}

function Increment-PatchVersion {
    param([string]$Version)
    $parts = $Version.Split(".")
    $patch = [int]$parts[2] + 1
    return "$($parts[0]).$($parts[1]).$patch"
}

if ($Help) {
    Write-Host @"
Tai-AI 发布脚本

用法: ./publish.ps1 [选项]

选项:
    -Configuration <Debug|Release>  构建配置 (默认: Release)
    -Platform <x64|x86|arm64>       目标平台 (默认: x64)
    -SkipTests                      跳过测试
    -IncrementVersion               自动增加版本号
    -Help                           显示帮助信息

示例:
    ./publish.ps1
    ./publish.ps1 -Configuration Debug -SkipTests
    ./publish.ps1 -IncrementVersion
"@
    exit 0
}

Write-Header "Tai-AI 发布脚本"

$version = Get-ProjectVersion
Write-Host "当前版本: $version" -ForegroundColor Green

Push-Location $ProjectRoot

try {
    Write-Step "1/5" "清理旧的发布文件..."
    $publishDir = Join-Path $ProjectRoot "publish"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir | Out-Null

    Write-Step "2/5" "还原 NuGet 包..."
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet 包还原失败"
    }

    if (-not $SkipTests) {
        Write-Step "3/5" "运行测试..."
        dotnet test src/Tai.Tests/Tai.Tests.csproj -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "测试失败，是否继续发布? (Y/N)"
            $continue = Read-Host
            if ($continue -ne "Y") {
                throw "发布已取消"
            }
        }
    }

    Write-Step "4/5" "构建项目..."
    dotnet build src/Tai.App/Tai.App.csproj -c $Configuration -p:Platform=$Platform --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败"
    }

    Write-Step "5/5" "发布应用..."
    dotnet publish src/Tai.App/Tai.App.csproj `
        -c $Configuration `
        -p:Platform=$Platform `
        --no-build `
        -p:PublishProfile=win-$Platform
    
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败"
    }

    if ($IncrementVersion) {
        $newVersion = Increment-PatchVersion -Version $version
        Set-ProjectVersion -Version $newVersion
        Write-Host "版本已更新: $version -> $newVersion" -ForegroundColor Green
    }

    $outputPath = "publish"
    Write-Header "发布完成!"
    Write-Host "输出目录: $ProjectRoot\$outputPath" -ForegroundColor Green
    
    $exePath = Join-Path $outputPath "PChabit.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Host "文件大小: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Gray
    }

} catch {
    Write-Error "发布失败: $_"
    exit 1
} finally {
    Pop-Location
}
