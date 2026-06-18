---
name: winui3-xaml-silent-fail
description: |
  WinUI 3 XAML 编译器 (XamlCompiler.exe) 静默失败时的诊断流程。
  当 `dotnet build` 输出 `error MSB3073` 但 stderr 为空，exit code 1，
  几乎所有错误信息都没显示时使用此 skill。
---

# WinUI 3 XAML 编译器静默失败诊断

## 症状

```
error MSB3073: 命令“"XamlCompiler.exe" "obj\...input.json" "obj\...output.json"”已退出，代码为 1。
```

- 无 stderr 输出
- `output.json` 中 `MSBuildLogEntries` 只有性能标记（perfXC_*），无错误条目
- `*.g.cs` 文件 0 字节，Pass 2 未完成
- 通常同时存在 1-2 个 C# 错误（被 XamlCompiler 屏蔽）

## 已知触发条件

### 1. DatePicker.SelectedDate ↔ DateTime 类型不匹配（最常见）
**错误用法**：
```xml
<DatePicker SelectedDate="{x:Bind ViewModel.MyDate, Mode=TwoWay}" />
```
ViewModel 中 `MyDate` 是 `DateTime`，但 `DatePicker.SelectedDate` 是 `DateTimeOffset?`。

**修复**：
- 改用 `CalendarDatePicker`（`Date` 属性接受 `DateTime?`）
- 或添加 `DateTimeOffset?` 桥接属性：
  ```csharp
  public DateTimeOffset? MyDateOffset
  {
      get => new DateTimeOffset(MyDate);
      set { if (value.HasValue) MyDate = value.Value.Date; }
  }
  ```

### 2. `<Run Text="{Binding LongProperty}" />` 类型不匹配
**错误用法**：
```xml
<TextBlock><Run Text="{Binding Size}" /></TextBlock>  <!-- Size is long -->
```
`Run.Text` 是 `string`，不能直接绑定 `long/bool/int?/DateTime?`。

**修复**：在 ViewModel/Entity 类添加 `FormattedXxx` 计算属性：
```csharp
public string FormattedSize => Size switch
{
    < 1024 => $"{Size} B",
    < 1024*1024 => $"{Size/1024.0:F1} KB",
    _ => $"{Size/1024.0/1024.0:F1} MB"
};
```
然后 `Text="{Binding FormattedSize}"`。

### 3. 代码-behind 引用 XAML 中尚未声明的 x:Name
**错误用法**：
```csharp
// DataManagementPage.xaml.cs
WebDAVUrlBox.Text = ViewModel.WebDAVUrl;  // WebDAVUrlBox 在 XAML 中不存在
```

**修复**：
- 在 XAML 添加 `x:Name="WebDAVUrlBox"`，或
- 从代码-behind 移除引用，或
- 用 `WebDAVUrlBox?.Text ?? ""` 防御

### 4. Run + 父 TextBlock.Text 同时存在
```xml
<!-- 错误：XAML 不允许 TextBlock.Text 和子 Run 同时存在 -->
<TextBlock Text="静态文本">
    <Run Text="{Binding Xxx}" />
</TextBlock>
```

## 诊断流程

1. **先 `minimal XAML` 测试**：把整个 Page 替换为 `<TextBlock Text="placeholder" />`
   - 如果能编译，问题在 XAML
   - 如果仍然失败，问题在 csproj/global.json/SDK

2. **逐步加回 XAML 节点 + 同步代码-behind**：
   ```
   minimal → +Page 根 → +Grid → +第一个 Border → ...
   ```
   每次 `dotnet build src/PChabit.App/PChabit.App.csproj -c Release -p:Platform=x64`

3. **检查 obj/.../output.json 的 MSBuildLogEntries**：
   ```bash
   cat obj/.../output.json | python -c "
   import json, sys
   d = json.load(sys.stdin)
   for m in d.get('MSBuildLogEntries', []):
       print(m.get('Message',''))
   "
   ```
   如果没有 XAML 错误条目，是编译器崩溃；如果有 XAML 错误条目，会包含具体行号。

4. **运行 XamlCompiler.exe 直接测试**：
   ```bash
   "C:/Users/idlee/.nuget/packages/microsoft.windowsappsdk/1.4.231115000/tools/net472/XamlCompiler.exe" \
     obj/.../input.json obj/.../output.json
   echo "EXIT=$?"
   ```
   - Exit 0 = 成功
   - Exit 1 = 静默失败（按上面已知条件排查）

## 预防

写 XAML 时：
- **永远不**用 `<Run Text="{Binding LongXxx}" />`
- DateTime 字段同时提供 `DateTimeOffset?` 桥接属性
- 任何 `x:Name` 修改必须立即同步代码-behind
- **永远不**用 `DatePicker` 绑定 `DateTime` 字段，用 `CalendarDatePicker`

## 相关文件位置
- XamlCompiler.exe: `C:/Users/idlee/.nuget/packages/microsoft.windowsappsdk/2.2.1/tools/net472/XamlCompiler.exe`
- input.json: `obj/x64/Release/net9.0-windows10.0.22621.0/input.json`
- output.json: `obj/x64/Release/net9.0-windows10.0.22621.0/output.json`
