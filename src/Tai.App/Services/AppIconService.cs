using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;

namespace Tai.App.Services;

public interface IAppIconService
{
    Task<SoftwareBitmapSource?> GetAppIconAsync(string processName, int size = 32);
    Task<Dictionary<string, SoftwareBitmapSource?>> GetIconsBatchAsync(IEnumerable<string> processNames, int size = 20, CancellationToken cancellationToken = default);
}

public class AppIconService : IAppIconService
{
    private readonly Dictionary<string, SoftwareBitmapSource> _iconCache = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, bool> _failedLookupCache = new();
    private readonly object _failedLookupLock = new();
    private readonly SemaphoreSlim _semaphore = new(8);
    
    public async Task<SoftwareBitmapSource?> GetAppIconAsync(string processName, int size = 32)
    {
        if (string.IsNullOrEmpty(processName))
            return null;
        
        var cacheKey = $"{processName}_{size}";
        
        lock (_cacheLock)
        {
            if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
                return cachedIcon;
        }
        
        lock (_failedLookupLock)
        {
            if (_failedLookupCache.TryGetValue(cacheKey, out var failed) && failed)
                return null;
        }
        
        await _semaphore.WaitAsync();
        try
        {
            lock (_cacheLock)
            {
                if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
                    return cachedIcon;
            }
            
            lock (_failedLookupLock)
            {
                if (_failedLookupCache.TryGetValue(cacheKey, out var failed) && failed)
                    return null;
            }
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var icon = await GetProcessIconAsync(processName, size);
            sw.Stop();
            Log.Debug("[AppIconService] GetProcessIconAsync {ProcessName}, 耗时: {ElapsedMs}ms, 结果: {Result}", processName, sw.ElapsedMilliseconds, icon != null);
            
            if (icon != null)
            {
                lock (_cacheLock)
                {
                    _iconCache[cacheKey] = icon;
                }
                return icon;
            }
            else
            {
                lock (_failedLookupLock)
                {
                    _failedLookupCache[cacheKey] = true;
                }
                Log.Debug("[AppIconService] 无法获取图标: {ProcessName}", processName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppIconService] 获取图标失败: {ProcessName}", processName);
        }
        finally
        {
            _semaphore.Release();
        }
        
        return null;
    }
    
    public async Task<Dictionary<string, SoftwareBitmapSource?>> GetIconsBatchAsync(IEnumerable<string> processNames, int size = 20, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var nameList = processNames.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        Log.Information("[AppIconService] GetIconsBatchAsync 开始, 数量: {Count}", nameList.Count);
        
        var results = new Dictionary<string, SoftwareBitmapSource?>();
        
        try
        {
            var tasks = nameList.Select(async name =>
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    var icon = await GetAppIconAsync(name, size).ConfigureAwait(false);
                    return (name, icon);
                }
                catch (OperationCanceledException)
                {
                    return (name, (SoftwareBitmapSource?)null);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[AppIconService] 获取图标失败: {ProcessName}", name);
                    return (name, (SoftwareBitmapSource?)null);
                }
            });
            
            var completed = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var (name, icon) in completed)
            {
                results[name] = icon;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("[AppIconService] GetIconsBatchAsync 被取消");
        }
        
        sw.Stop();
        Log.Information("[AppIconService] GetIconsBatchAsync 完成, 耗时: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return results;
    }
    
    private async Task<SoftwareBitmapSource?> GetProcessIconAsync(string processName, int size)
    {
        var processPath = await Task.Run(() => GetProcessPath(processName));
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
        {
            Log.Debug("[AppIconService] 找不到进程路径: {ProcessName}", processName);
            return null;
        }
        
        var hIcon = ExtractIcon(IntPtr.Zero, processPath, 0);
        if (hIcon == IntPtr.Zero)
        {
            Log.Debug("[AppIconService] ExtractIcon 返回空: {ProcessPath}", processPath);
            return null;
        }
        
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            var resizedBitmap = new System.Drawing.Bitmap(bitmap, new System.Drawing.Size(size, size));
            
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            
            var buffer = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var pixel = resizedBitmap.GetPixel(x, y);
                    var idx = (y * size + x) * 4;
                    buffer[idx] = pixel.B;
                    buffer[idx + 1] = pixel.G;
                    buffer[idx + 2] = pixel.R;
                    buffer[idx + 3] = pixel.A;
                }
            }
            
            resizedBitmap.Dispose();
            
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)size, (uint)size, 96, 96, buffer);
            await encoder.FlushAsync();
            
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            
            Log.Debug("[AppIconService] 成功获取图标: {ProcessName} -> {Path}", processName, processPath);
            return source;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
    
    private string? GetProcessPath(string processName)
    {
        var processNameWithoutExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
            ? processName[..^4] 
            : processName;
        
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processNameWithoutExt);
            if (processes.Length > 0)
            {
                var path = processes[0].MainModule?.FileName;
                foreach (var p in processes)
                    p.Dispose();
                if (!string.IsNullOrEmpty(path))
                {
                    Log.Debug("[AppIconService] 从运行进程获取路径: {ProcessName} -> {Path}", processName, path);
                    return path;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("[AppIconService] 获取运行进程路径失败: {ProcessName}, 错误: {Error}", processName, ex.Message);
        }
        
        var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var appPaths = GetAppPathsFromRegistry(processNameWithoutExt);
        
        var possiblePaths = new List<string>
        {
            Path.Combine(systemPath, $"{processNameWithoutExt}.exe"),
        };
        
        possiblePaths.AddRange(appPaths);
        
        possiblePaths.AddRange(new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), processNameWithoutExt, $"{processNameWithoutExt}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), processNameWithoutExt, $"{processNameWithoutExt}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", processNameWithoutExt, $"{processNameWithoutExt}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), processNameWithoutExt, $"{processNameWithoutExt}.exe"),
        });
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Log.Debug("[AppIconService] 从路径查找获取: {ProcessName} -> {Path}", processName, path);
                return path;
            }
        }
        
        return null;
    }
    
    private List<string> GetAppPathsFromRegistry(string processName)
    {
        var paths = new List<string>();
        
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{processName}.exe");
            if (key?.GetValue("") is string path && File.Exists(path))
            {
                paths.Add(path);
            }
        }
        catch { }
        
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{processName}.exe");
            if (key?.GetValue("") is string path && File.Exists(path))
            {
                paths.Add(path);
            }
        }
        catch { }
        
        return paths;
    }
    
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
