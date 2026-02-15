using System.Diagnostics;
using Tai.Infrastructure.Helpers;

namespace Tai.Infrastructure.Services;

public class AppInfoResolver
{
    private static readonly TimeSpan ProcessInfoTimeout = TimeSpan.FromMilliseconds(500);
    
    public AppInfo Resolve(IntPtr windowHandle)
    {
        var processId = Win32Helper.GetProcessIdFromWindow(windowHandle);
        var windowTitle = Win32Helper.GetWindowTitle(windowHandle);
        var windowClass = Win32Helper.GetWindowClassName(windowHandle);
        var bounds = Win32Helper.GetWindowBounds(windowHandle);
        var isMaximized = Win32Helper.IsZoomed(windowHandle);
        
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
                Debug.WriteLine($"获取进程路径失败: {ex.Message}");
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取进程信息失败: {ex.Message}");
        }
        
        return new AppInfo
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
    }
    
    private static string GetExecutablePathSafely(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
    
    private static FileVersionInfo? GetVersionInfoSafely(string executablePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

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
