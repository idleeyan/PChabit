using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;

namespace PChabit.App.Services;

public interface IAppIconService
{
    Task<SoftwareBitmapSource?> GetAppIconAsync(string processName, int size = 32);
    Task<Dictionary<string, SoftwareBitmapSource?>> GetIconsBatchAsync(IEnumerable<string> processNames, int size = 20, CancellationToken cancellationToken = default);
}

public class AppIconService : IAppIconService
{
    private const int MaxCacheSize = 500;
    private readonly Dictionary<string, SoftwareBitmapSource> _iconCache = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, bool> _failedLookupCache = new();
    private readonly object _failedLookupLock = new();
    private readonly SemaphoreSlim _semaphore = new(8);
    private readonly Queue<string> _cacheKeyQueue = new();
    private readonly Queue<string> _failedKeyQueue = new();
    
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
                    _cacheKeyQueue.Enqueue(cacheKey);
                    TrimCacheIfNeeded();
                }
                return icon;
            }
            else
            {
                lock (_failedLookupLock)
                {
                    _failedLookupCache[cacheKey] = true;
                    _failedKeyQueue.Enqueue(cacheKey);
                    TrimFailedCacheIfNeeded();
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
    
    private void TrimCacheIfNeeded()
    {
        while (_iconCache.Count > MaxCacheSize && _cacheKeyQueue.Count > 0)
        {
            var oldestKey = _cacheKeyQueue.Dequeue();
            _iconCache.Remove(oldestKey);
        }
    }
    
    private void TrimFailedCacheIfNeeded()
    {
        while (_failedLookupCache.Count > MaxCacheSize && _failedKeyQueue.Count > 0)
        {
            var oldestKey = _failedKeyQueue.Dequeue();
            _failedLookupCache.Remove(oldestKey);
        }
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
            using var resizedBitmap = new System.Drawing.Bitmap(bitmap, new System.Drawing.Size(size, size));
            
            // 使用 LockBits + Marshal.Copy 替代逐像素 GetPixel，性能提升 10-100 倍
            var bitmapData = resizedBitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, size, size),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            byte[] buffer;
            try
            {
                var stride = bitmapData.Stride;
                buffer = new byte[size * size * 4];
                
                // 逐行复制，处理可能的 stride 填充
                for (int y = 0; y < size; y++)
                {
                    var sourceOffset = y * stride;
                    var destOffset = y * size * 4;
                    Marshal.Copy(bitmapData.Scan0 + sourceOffset, buffer, destOffset, size * 4);
                }
                
                // BGRA 格式已与 BitmapEncoder.Bgra8 匹配，无需逐像素转换
            }
            finally
            {
                resizedBitmap.UnlockBits(bitmapData);
            }
            
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            
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

        // 1) 注册表 App Paths（系统安装的软件）
        var appPaths = GetAppPathsFromRegistry(processNameWithoutExt);
        if (appPaths.Count > 0)
            return appPaths[0];

        // 2) 正在运行的进程（最准确）
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processNameWithoutExt);
            if (processes.Length == 0 && processNameWithoutExt.Contains(' '))
            {
                // 进程名含空格时 GetProcessesByName 常失败，改用快照扫描
                processes = System.Diagnostics.Process.GetProcesses()
                    .Where(p =>
                    {
                        try { return string.Equals(p.ProcessName, processNameWithoutExt, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    })
                    .ToArray();
            }

            if (processes.Length > 0)
            {
                try
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            var path = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path))
                            {
                                Log.Debug("[AppIconService] 从运行进程获取路径: {ProcessName} -> {Path}", processName, path);
                                return path;
                            }
                        }
                        catch
                        {
                            // 访问被拒绝时继续尝试下一个实例
                        }
                    }
                }
                finally
                {
                    foreach (var p in processes)
                        p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("[AppIconService] 获取运行进程路径失败: {ProcessName}, 错误: {Error}", processName, ex.Message);
        }

        // 3) 注册表 Uninstall DisplayIcon（很多 Electron/绿色软件不在 App Paths）
        var uninstallIcon = GetPathFromUninstallKeys(processNameWithoutExt);
        if (!string.IsNullOrEmpty(uninstallIcon) && File.Exists(uninstallIcon))
            return uninstallIcon;

        // 4) 常见安装位置扫描（含 D:\Tool、LocalAppData\Programs）
        var found = SearchCommonInstallPaths(processNameWithoutExt);
        if (found != null)
            return found;

        return null;
    }

    private string? SearchCommonInstallPaths(string processNameWithoutExt)
    {
        var exeName = processNameWithoutExt + ".exe";
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Programs"),
            @"D:\Tool",
            @"D:\Program Files",
            @"D:\Program Files (x86)",
            @"E:\Tool",
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            // 直接子目录：root\App\App.exe
            try
            {
                var direct = Path.Combine(root, processNameWithoutExt, exeName);
                if (File.Exists(direct)) return direct;

                // 名称大小写/连字符变体
                foreach (var variant in new[] { processNameWithoutExt, processNameWithoutExt.Replace(' ', '-'), processNameWithoutExt.Replace(" ", "") })
                {
                    var p = Path.Combine(root, variant, exeName);
                    if (File.Exists(p)) return p;
                    p = Path.Combine(root, variant, variant + ".exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // 深度 2 的有限扫描（避免全盘）
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate)) return candidate;

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            candidate = Path.Combine(sub, exeName);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return null;
    }

    private static string? GetPathFromUninstallKeys(string processNameWithoutExt)
    {
        var hives = new[]
        {
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var hive in hives)
        {
            if (hive == null) continue;
            try
            {
                foreach (var subName in hive.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = hive.OpenSubKey(subName);
                        if (sub == null) continue;

                        var displayName = sub.GetValue("DisplayName") as string ?? "";
                        var displayIcon = sub.GetValue("DisplayIcon") as string;
                        var installLocation = sub.GetValue("InstallLocation") as string;

                        var nameMatch =
                            displayName.Contains(processNameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                            processNameWithoutExt.Contains(displayName, StringComparison.OrdinalIgnoreCase) &&
                            displayName.Length >= 3;

                        if (!nameMatch) continue;

                        if (!string.IsNullOrEmpty(displayIcon))
                        {
                            // DisplayIcon 可能是 "C:\path\app.exe,0"
                            var iconPath = displayIcon.Split(',')[0].Trim().Trim('"');
                            if (File.Exists(iconPath)) return iconPath;
                        }

                        if (!string.IsNullOrEmpty(installLocation))
                        {
                            var candidate = Path.Combine(installLocation.TrimEnd('\\'), processNameWithoutExt + ".exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                hive.Dispose();
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
