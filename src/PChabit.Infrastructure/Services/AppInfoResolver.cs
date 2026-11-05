using System.Collections.Concurrent;
using System.Diagnostics;
using PChabit.Infrastructure.Helpers;
using Serilog;

namespace PChabit.Infrastructure.Services;

public class AppInfoResolver
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<uint, ProcessInfoCache> _processCache = new();

    public AppInfo Resolve(IntPtr windowHandle)
    {
        var processId = Win32Helper.GetProcessIdFromWindow(windowHandle);
        var windowTitle = Win32Helper.GetWindowTitle(windowHandle);
        var windowClass = Win32Helper.GetWindowClassName(windowHandle);
        var bounds = Win32Helper.GetWindowBounds(windowHandle);
        var isMaximized = Win32Helper.IsZoomed(windowHandle);

        ProcessInfoCache? cached = null;
        bool useCache = false;

        if (_processCache.TryGetValue(processId, out cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < CacheExpiration)
            {
                useCache = true;
            }
            else
            {
                _processCache.TryRemove(processId, out _);
            }
        }

        if (useCache && cached != null)
        {
            return new AppInfo
            {
                ProcessName = cached.ProcessName,
                ExecutablePath = cached.ExecutablePath,
                AppName = cached.AppName,
                AppVersion = cached.AppVersion,
                Publisher = cached.Publisher,
                WindowTitle = windowTitle,
                WindowClass = windowClass,
                WindowX = bounds.X,
                WindowY = bounds.Y,
                WindowWidth = bounds.Width,
                WindowHeight = bounds.Height,
                IsMaximized = isMaximized
            };
        }

        string processName = "Unknown";
        string executablePath = string.Empty;
        string appName = string.Empty;
        string? appVersion = null;
        string? publisher = null;

        try
        {
            var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                executablePath = GetExecutablePathSafely(process);

                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    var versionInfo = GetVersionInfoSafely(executablePath);
                    if (versionInfo != null)
                    {
                        appName = versionInfo.FileDescription ?? processName;
                        appVersion = versionInfo.FileVersion;
                        publisher = versionInfo.CompanyName;
                    }
                }
            }
            catch (Exception ex)
            {
                // 预期：路径/版本信息读取失败（权限受限），降级为仅用进程名
                Log.Debug(ex, "Executable path/version info read failed for PID {Pid}", processId);
            }
        }
        catch (ArgumentException ex)
        {
            // 预期：前台切换瞬间目标进程已退出（竞态，高频），缓存淘汰后下轮自动修正
            Log.Debug(ex, "Process exited during resolve (ArgumentException), PID {Pid}", processId);
        }
        catch (InvalidOperationException ex)
        {
            // 预期：同上，进程已退出
            Log.Debug(ex, "Process exited during resolve (InvalidOperationException), PID {Pid}", processId);
        }
        catch (Exception ex)
        {
            // 兜底：任何解析失败都降级为 "Unknown"，不阻塞采集主流程
            Log.Debug(ex, "App info resolve failed, PID {Pid}", processId);
        }

        var result = new AppInfo
        {
            ProcessName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : $"{processName}.exe",
            ExecutablePath = executablePath,
            AppName = string.IsNullOrEmpty(appName) ? processName : appName,
            AppVersion = appVersion,
            Publisher = publisher,
            WindowTitle = windowTitle,
            WindowClass = windowClass,
            WindowX = bounds.X,
            WindowY = bounds.Y,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            IsMaximized = isMaximized
        };

        _processCache[processId] = new ProcessInfoCache(
            result.ProcessName,
            result.ExecutablePath,
            result.AppName,
            result.AppVersion,
            result.Publisher,
            DateTime.UtcNow
        );

        return result;
    }

    public static void RemoveFromCache(uint processId)
    {
        _processCache.TryRemove(processId, out _);
    }

    public static void ClearCache()
    {
        _processCache.Clear();
    }

    private static string GetExecutablePathSafely(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 预期：权限受限进程（高频竞态），降级为空路径
            Log.Debug(ex, "读取 MainModule 被拒绝（权限受限）");
            return string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            // 预期：进程已退出（高频竞态）
            Log.Debug(ex, "读取 MainModule 失败（进程已退出）");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "读取 MainModule 失败");
            return string.Empty;
        }
    }

    private static FileVersionInfo? GetVersionInfoSafely(string executablePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath);
        }
        catch (Exception ex)
        {
            // 预期：文件被占用/路径失效，版本信息降级为 null
            Log.Debug(ex, "读取版本信息失败: {Path}", executablePath);
            return null;
        }
    }
}

public sealed record ProcessInfoCache(
    string ProcessName,
    string ExecutablePath,
    string AppName,
    string? AppVersion,
    string? Publisher,
    DateTime CachedAt
);

public class AppInfo
{
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? Publisher { get; set; }
    public string WindowTitle { get; set; } = string.Empty;
    public string WindowClass { get; set; } = string.Empty;
    public double WindowX { get; set; }
    public double WindowY { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool IsMaximized { get; set; }
}
