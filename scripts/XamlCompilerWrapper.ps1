# XamlCompiler 包装脚本：强制 en-US 文化绕过中文资源加载问题
param(
    [string]$InputJson,
    [string]$OutputJson
)

# 强制设置英文 UI 文化
[System.Threading.Thread]::CurrentThread.CurrentUICulture = 'en-US'
[System.Globalization.CultureInfo]::DefaultThreadCurrentUICulture = 'en-US'

# 找到真正的 XamlCompiler.exe
$toolsDir = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk.winui"
$winuiDir = Get-ChildItem $toolsDir -Directory | Sort-Object Name -Descending | Select-Object -First 1
$xamlCompiler = Join-Path $winuiDir.FullName "tools\net6.0\Microsoft.UI.Xaml.Markup.Compiler.dll"

# 尝试用 dotnet 运行托管的 XAML 编译器
$result = dotnet $xamlCompiler $InputJson $OutputJson 2>&1
$exitCode = $LASTEXITCODE

# 输出结果
Write-Host $result
exit $exitCode
