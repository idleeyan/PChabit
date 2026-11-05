using Serilog;

namespace PChabit.Infrastructure.Services;

public partial class SettingsService
{
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ApplyStartupSetting()
    {
        // 先解析目标 exe：ProcessPath 优先，缺省回退到 BaseDirectory
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "PChabit.exe");
        if (!File.Exists(exePath))
        {
            Log.Warning("自启动目标不存在: {Path}", exePath);
            // 目标丢了仍尝试清理残留项，避免留下指向失效路径的自启
            RemoveStartupEntries();
            return;
        }

        var quotedExe = "\"" + exePath + "\"";
        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = string.IsNullOrEmpty(startupFolderPath)
            ? null
            : Path.Combine(startupFolderPath, "PChabit.lnk");
        var legacyShortcutPath = string.IsNullOrEmpty(startupFolderPath)
            ? null
            : Path.Combine(startupFolderPath, "Tai.lnk");

        if (!StartWithWindows)
        {
            RemoveStartupEntries();
            return;
        }

        // 1) 注册表 Run（最可靠，开机由 Explorer 直接拉起，不依赖 .lnk）
        try
        {
            using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey == null)
            {
                Log.Warning("无法打开 HKCU Run 键，开机自启降级为快捷方式");
            }
            else
            {
                var current = runKey.GetValue("PChabit") as string;
                if (!string.Equals(current, quotedExe, StringComparison.Ordinal))
                {
                    runKey.SetValue("PChabit", quotedExe);
                    Log.Information("已写入开机自启(注册表 Run): {Path}", exePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写入注册表开机自启失败");
        }

        // 2) 启动文件夹快捷方式（兼容旧路径清理 + 双保险）
        try
        {
            if (legacyShortcutPath != null && File.Exists(legacyShortcutPath))
            {
                File.Delete(legacyShortcutPath);
                Log.Information("已清理旧自启动快捷方式 Tai.lnk");
            }

            if (shortcutPath != null)
            {
                // 仅在缺失或目标不对时重建，避免每次 Load/Save 都删写
                if (!ShortcutTargets(shortcutPath, exePath))
                {
                    // 先写临时文件再替换，避免「先删后写」失败导致自启丢失
                    var tempPath = shortcutPath + ".tmp.lnk";
                    CreateShortcut(tempPath, exePath);
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    File.Move(tempPath, shortcutPath);
                    Log.Information("已写入开机自启动: {Shortcut} -> {Target}", shortcutPath, exePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "写入开机自启动快捷方式失败");
        }
    }

    private void RemoveStartupEntries()
    {
        try
        {
            using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("PChabit", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "移除注册表开机自启失败");
        }

        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrEmpty(startupFolderPath)) return;

        foreach (var name in new[] { "PChabit.lnk", "Tai.lnk", "PChabit.lnk.tmp.lnk" })
        {
            try
            {
                var p = Path.Combine(startupFolderPath, name);
                if (File.Exists(p))
                {
                    File.Delete(p);
                    Log.Information("已移除开机自启动: {Path}", p);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "移除开机自启动快捷方式失败: {Name}", name);
            }
        }
    }

    /// <summary>快捷方式是否存在且指向期望的 exe。</summary>
    private static bool ShortcutTargets(string shortcutPath, string expectedExe)
    {
        if (!File.Exists(shortcutPath)) return false;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            dynamic? shell = null;
            dynamic? shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return false;
                shortcut = shell.GetType().InvokeMember(
                    "CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                if (shortcut == null) return false;
                var target = shortcut.GetType().InvokeMember(
                    "TargetPath", System.Reflection.BindingFlags.GetProperty,
                    null, shortcut, null) as string;
                return string.Equals(target, expectedExe, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            // 预期：COM 反射校验失败（Shell 繁忙/权限），返回 false 触发上游重建快捷方式
            Log.Debug(ex, "快捷方式校验失败，将按需重建");
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                Log.Warning("WScript.Shell 不可用，无法创建自启动快捷方式");
                return;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                Log.Warning("WScript.Shell 实例创建失败");
                return;
            }

            shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null)
            {
                Log.Warning("CreateShortcut 返回空: {Path}", shortcutPath);
                return;
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetPath) ?? string.Empty });
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "PChabit Activity Tracker" });
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut != null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            }
            if (shell != null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }
}
