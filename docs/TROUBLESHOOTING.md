# PChabit 项目问题解决记录

## 概述

本文档记录了 PChabit 项目在构建和发布过程中遇到的问题及其解决方案。

---

## 问题 1: .NET SDK 版本冲突

### 问题描述

项目使用 .NET 8.0，但系统中安装了 .NET 10.0 预览版 SDK，导致构建时出现兼容性问题。

### 错误信息

```
error MSB4062: 未能从程序集加载任务"Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent"
系统找不到指定的路径。
```

### 解决方案

1. 删除预览版 SDK：
   ```powershell
   Remove-Item -Path "C:\Program Files\dotnet\sdk\10.0.100-preview.1.25120.13" -Recurse -Force
   ```

2. 安装 .NET 8.0 SDK：
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```

3. 更新 `global.json`：
   ```json
   {
     "sdk": {
       "version": "8.0.408",
       "rollForward": "latestMinor"
     }
   }
   ```

---

## 问题 2: PRI 资源生成任务缺失

### 问题描述

Windows App SDK 构建 PRI 资源文件需要 MSIX 打包组件，但 Visual Studio 默认未安装此组件。

### 错误信息

```
error MSB4062: 未能从程序集加载任务"Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent"
Could not load file or assembly 'Microsoft.Build.Packaging.Pri.Tasks.dll'
```

### 解决方案

1. 安装 Visual Studio UWP 工作负载：
   ```powershell
   & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify `
     --installPath "C:\Program Files\Microsoft Visual Studio\2022\Community" `
     --add Microsoft.VisualStudio.Workload.Universal `
     --passive
   ```

2. 复制 PRI 任务文件到 .NET SDK 目录：
   ```powershell
   $sdkPath = "C:\Program Files\dotnet\sdk\8.0.408\Microsoft\VisualStudio\v17.0\AppxPackage"
   New-Item -ItemType Directory -Path $sdkPath -Force
   Copy-Item "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\*" `
     -Destination $sdkPath -Recurse -Force
   ```

---

## 问题 3: IDbContextFactory 未注册

### 问题描述

Entity Framework Core 的 `IDbContextFactory<TContext>` 未在依赖注入容器中注册，导致运行时错误。

### 错误信息

```
System.InvalidOperationException: No service for type 'Microsoft.EntityFrameworkCore.IDbContextFactory`1[Tai.Infrastructure.Data.TaiDbContext]' has been registered.
```

### 解决方案

在 `ServiceCollectionExtensions.cs` 中添加 `AddDbContextFactory`：

```csharp
public static IServiceCollection ConfigureServices(this IServiceCollection services, string databasePath)
{
    services.AddDbContext<TaiDbContext>(options =>
        options.UseSqlite($"Data Source={databasePath}"));
    
    // 添加 DbContextFactory 支持
    services.AddDbContextFactory<TaiDbContext>(options =>
        options.UseSqlite($"Data Source={databasePath}"));
    
    // ... 其他服务注册
}
```

---

## 问题 4: Windows App Runtime 缺失

### 问题描述

WinUI 3 应用需要 Windows App Runtime 运行时组件，但系统未安装或版本不匹配。

### 错误信息

```
APPCRASH: Microsoft.UI.Xaml.dll
异常代码: 0xc000027b
```

### 解决方案

安装 Windows App Runtime MSIX 包：

```powershell
# 安装 Main 包
Add-AppxPackage -Path "C:\Users\{用户}\.nuget\packages\microsoft.windowsappsdk\1.4.231115000\tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.Main.1.4.msix"

# 安装 Singleton 包
Add-AppxPackage -Path "C:\Users\{用户}\.nuget\packages\microsoft.windowsappsdk\1.4.231115000\tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.Singleton.1.4.msix"

# 安装 DDLM 包
Add-AppxPackage -Path "C:\Users\{用户}\.nuget\packages\microsoft.windowsappsdk\1.4.231115000\tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.DDLM.1.4.msix"
```

---

## 问题 5: XAML 资源文件缺失

### 问题描述

使用 `dotnet publish` 发布 WinUI 3 应用时，XAML 编译后的资源文件（.xbf 和 .pri）未复制到发布目录。

### 错误信息

```
Microsoft.UI.Xaml.Markup.XamlParseException: XAML parsing failed.
at Tai.App.Views.ShellPage.InitializeComponent()
```

### 解决方案

手动复制 XAML 资源文件到发布目录：

```powershell
# 复制 XBF 文件
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\*.xbf" -Destination "publish" -Force

# 复制 PRI 文件
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\*.pri" -Destination "publish" -Force

# 复制 Views 目录下的 XBF 文件
New-Item -ItemType Directory -Path "publish\Views" -Force
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\Views\*.xbf" -Destination "publish\Views" -Force
```

---

## 问题 6: Windows App SDK 版本兼容性

### 问题描述

Windows App SDK 1.5 和 1.6 版本在某些系统上存在兼容性问题，导致应用崩溃。

### 错误信息

```
APPCRASH: Microsoft.UI.Xaml.dll
异常代码: 0xc000027b
```

### 解决方案

降级到更稳定的 Windows App SDK 1.4 版本：

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.4.231115000" />
```

---

## 推荐的项目配置

### Tai.App.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>
        <OutputType>WinExe</OutputType>
        <UseWinUI>true</UseWinUI>
        <Platforms>x64</Platforms>
        <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
        <EnableMsixTooling>false</EnableMsixTooling>
        <WindowsPackageType>None</WindowsPackageType>
        <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
        <WindowsAppSDKUndockedRegFreeWinRTInitializeAtStartup>true</WindowsAppSDKUndockedRegFreeWinRTInitializeAtStartup>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.4.231115000" />
    </ItemGroup>
</Project>
```

---

## 完整发布流程

```powershell
# 1. 清理旧构建文件
Remove-Item -Recurse -Force "src\*\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "src\*\obj" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "publish" -ErrorAction SilentlyContinue

# 2. 发布应用
dotnet publish src/Tai.App/Tai.App.csproj -c Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true --output publish

# 3. 复制 XAML 资源文件
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\*.xbf" -Destination "publish" -Force
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\*.pri" -Destination "publish" -Force
New-Item -ItemType Directory -Path "publish\Views" -Force | Out-Null
Copy-Item "src\Tai.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\Views\*.xbf" -Destination "publish\Views" -Force

# 4. 验证发布
Start-Process "publish\PChabit.exe"
```

---

## 环境要求

| 组件 | 版本 |
|------|------|
| .NET SDK | 8.0.x |
| Windows App SDK | 1.4.231115000 |
| Visual Studio | 2022 Community |
| Visual Studio 工作负载 | UWP 开发 |
| Windows 版本 | 10.0.22000.0 或更高 |

---

## 参考链接

- [Windows App SDK 文档](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [WinUI 3 应用发布指南](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Entity Framework Core DbContextFactory](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)

---

*文档创建时间: 2026-02-14*
