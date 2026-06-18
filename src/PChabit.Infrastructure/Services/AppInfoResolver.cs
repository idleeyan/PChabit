using System.Collections.Concurrent;
using System.Diagnostics;
using PChabit.Infrastructure.Helpers;

namespace PChabit.Infrastructure.Services;

public class AppInfoResolver
{
    private static readonly TimeSpan ProcessInfoTimeout = TimeSpan.FromMilliseconds(500);
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
            catch { }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch { }

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
