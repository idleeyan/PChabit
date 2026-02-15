using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Tai.App.Services;

public interface IAppIconService
{
    Task<SoftwareBitmapSource?> GetAppIconAsync(string processName, int size = 32);
}

public class AppIconService : IAppIconService
{
    private readonly Dictionary<string, SoftwareBitmapSource> _iconCache = new();
    private readonly object _cacheLock = new();
    
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
        
        try
        {
            var icon = await GetProcessIconAsync(processName, size);
            if (icon != null)
            {
                lock (_cacheLock)
                {
                    _iconCache[cacheKey] = icon;
                }
                return icon;
            }
        }
        catch
        {
        }
        
        return null;
    }
    
    private async Task<SoftwareBitmapSource?> GetProcessIconAsync(string processName, int size)
    {
        var processPath = GetProcessPath(processName);
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            return null;
        
        var hIcon = ExtractIcon(IntPtr.Zero, processPath, 0);
        if (hIcon == IntPtr.Zero)
            return null;
        
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
            
            return source;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
    
    private string? GetProcessPath(string processName)
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                var path = processes[0].MainModule?.FileName;
                foreach (var p in processes)
                    p.Dispose();
                return path;
            }
        }
        catch
        {
        }
        
        var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var possiblePaths = new[]
        {
            Path.Combine(systemPath, $"{processName}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), processName, $"{processName}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), processName, $"{processName}.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", processName, $"{processName}.exe"),
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        return null;
    }
    
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
