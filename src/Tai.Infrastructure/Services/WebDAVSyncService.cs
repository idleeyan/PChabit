using System.Net.Http;
using System.Xml.Linq;
using Serilog;
using Tai.Core.Interfaces;

namespace Tai.Infrastructure.Services;

public interface IWebDAVSyncService
{
    event EventHandler<WebDAVProgressEventArgs>? ProgressChanged;
    
    Task<bool> TestConnectionAsync(string url, string username, string password);
    Task<string?> UploadFileAsync(string url, string username, string password, string fileName, byte[] content);
    Task<string?> UploadFileWithProgressAsync(string url, string username, string password, string fileName, byte[] content, IProgress<int>? progress = null);
    Task<byte[]?> DownloadFileAsync(string url, string username, string password, string fileName);
    Task<byte[]?> DownloadFileWithProgressAsync(string url, string username, string password, string fileName, IProgress<int>? progress = null);
    Task<List<WebDAVFileInfo>> ListFilesAsync(string url, string username, string password, string? path = null);
    Task<bool> DeleteFileAsync(string url, string username, string password, string fileName);
    Task<bool> CreateFolderAsync(string url, string username, string password, string folderName);
}

public class WebDAVProgressEventArgs : EventArgs
{
    public string FileName { get; init; } = "";
    public int Progress { get; init; }
    public string Status { get; init; } = "";
    public bool IsUpload { get; init; }
}

public class WebDAVFileInfo
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long Size { get; init; }
    public DateTime? LastModified { get; init; }
    public bool IsDirectory { get; init; }
}

public class WebDAVSyncService : IWebDAVSyncService
{
    private readonly HttpClient _httpClient;
    
    public event EventHandler<WebDAVProgressEventArgs>? ProgressChanged;
    
    public WebDAVSyncService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
    }
    
    public async Task<bool> TestConnectionAsync(string url, string username, string password)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Options, url);
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request);
            Log.Information("WebDAV 连接测试: {StatusCode}", response.StatusCode);
            
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WebDAV 连接测试失败");
            return false;
        }
    }
    
    public async Task<string?> UploadFileAsync(string url, string username, string password, string fileName, byte[] content)
    {
        return await UploadFileWithProgressAsync(url, username, password, fileName, content, null);
    }
    
    public async Task<string?> UploadFileWithProgressAsync(string url, string username, string password, string fileName, byte[] content, IProgress<int>? progress = null)
    {
        try
        {
            var fullUrl = url.TrimEnd('/') + "/" + fileName;
            
            OnProgressChanged(fileName, 0, "开始上传...", true);
            progress?.Report(0);
            
            var parentPath = Path.GetDirectoryName(fileName)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentPath))
            {
                await CreateFolderPathAsync(url, username, password, parentPath);
            }
            
            var request = new HttpRequestMessage(HttpMethod.Put, fullUrl);
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentLength = content.Length;
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Created || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                OnProgressChanged(fileName, 100, "上传完成", true);
                progress?.Report(100);
                Log.Information("WebDAV 文件上传成功: {FileName}, 大小: {Size}", fileName, content.Length);
                return fullUrl;
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                Log.Warning("WebDAV 服务器不允许直接上传，尝试使用 MKCOL 创建目录后重试");
                
                var folderUrl = url.TrimEnd('/') + "/" + parentPath;
                var mkcolRequest = new HttpRequestMessage(new HttpMethod("MKCOL"), folderUrl + "/");
                AddAuthentication(mkcolRequest, username, password);
                var mkcolResponse = await _httpClient.SendAsync(mkcolRequest);
                
                if (mkcolResponse.IsSuccessStatusCode || mkcolResponse.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    var retryRequest = new HttpRequestMessage(HttpMethod.Put, fullUrl);
                    retryRequest.Content = new ByteArrayContent(content);
                    retryRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    AddAuthentication(retryRequest, username, password);
                    
                    var retryResponse = await _httpClient.SendAsync(retryRequest);
                    
                    if (retryResponse.IsSuccessStatusCode || retryResponse.StatusCode == System.Net.HttpStatusCode.Created)
                    {
                        OnProgressChanged(fileName, 100, "上传完成", true);
                        progress?.Report(100);
                        Log.Information("WebDAV 文件上传成功(重试): {FileName}", fileName);
                        return fullUrl;
                    }
                }
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            Log.Warning("WebDAV 文件上传失败: {StatusCode}, 错误: {Error}", response.StatusCode, errorContent);
            OnProgressChanged(fileName, 0, $"上传失败: {response.StatusCode}", true);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebDAV 文件上传异常: {FileName}", fileName);
            OnProgressChanged(fileName, 0, $"上传异常: {ex.Message}", true);
            return null;
        }
    }
    
    private async Task CreateFolderPathAsync(string url, string username, string password, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";
        
        foreach (var part in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;
            var folderUrl = url.TrimEnd('/') + "/" + currentPath;
            
            var request = new HttpRequestMessage(new HttpMethod("MKCOL"), folderUrl + "/");
            AddAuthentication(request, username, password);
            
            try
            {
                await _httpClient.SendAsync(request);
            }
            catch { }
        }
    }
    
    public async Task<byte[]?> DownloadFileAsync(string url, string username, string password, string fileName)
    {
        return await DownloadFileWithProgressAsync(url, username, password, fileName, null);
    }
    
    public async Task<byte[]?> DownloadFileWithProgressAsync(string url, string username, string password, string fileName, IProgress<int>? progress = null)
    {
        try
        {
            var fullUrl = url.TrimEnd('/') + "/" + fileName;
            
            OnProgressChanged(fileName, 0, "开始下载...", false);
            progress?.Report(0);
            
            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("WebDAV 文件下载失败: {StatusCode}", response.StatusCode);
                OnProgressChanged(fileName, 0, $"下载失败: {response.StatusCode}", false);
                return null;
            }
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes > 0 && progress != null;
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            
            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                
                if (canReportProgress)
                {
                    var percent = (int)((totalRead * 100) / totalBytes);
                    progress?.Report(percent);
                    OnProgressChanged(fileName, percent, $"下载中... {percent}%", false);
                }
            }
            
            OnProgressChanged(fileName, 100, "下载完成", false);
            progress?.Report(100);
            Log.Information("WebDAV 文件下载成功: {FileName}, 大小: {Size}", fileName, totalRead);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebDAV 文件下载异常: {FileName}", fileName);
            OnProgressChanged(fileName, 0, $"下载异常: {ex.Message}", false);
            return null;
        }
    }
    
    public async Task<List<WebDAVFileInfo>> ListFilesAsync(string url, string username, string password, string? path = null)
    {
        var files = new List<WebDAVFileInfo>();
        
        try
        {
            var listUrl = url;
            if (!string.IsNullOrEmpty(path))
            {
                listUrl = url.TrimEnd('/') + "/" + path.TrimStart('/');
            }
            
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), listUrl);
            request.Headers.Add("Depth", "1");
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("WebDAV 文件列表获取失败: {StatusCode}", response.StatusCode);
                return files;
            }
            
            var content = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrWhiteSpace(content))
            {
                Log.Warning("WebDAV 文件列表为空");
                return files;
            }
            
            var doc = XDocument.Parse(content);
            var ns = doc.Root?.GetDefaultNamespace();
            var davNs = ns ?? XNamespace.Get("DAV:");
            
            var responses = doc.Descendants()
                .Where(e => e.Name.LocalName == "response" || e.Name == XName.Get("response", davNs.NamespaceName))
                .ToList();
            
            var basePath = listUrl.TrimEnd('/');
            
            foreach (var responseElement in responses)
            {
                var hrefElement = responseElement.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "href" || e.Name == XName.Get("href", davNs.NamespaceName));
                
                if (hrefElement == null) continue;
                
                var hrefValue = Uri.UnescapeDataString(hrefElement.Value);
                var itemPath = hrefValue.TrimEnd('/');
                
                if (itemPath == basePath.TrimEnd('/')) continue;
                
                var name = Path.GetFileName(itemPath);
                if (string.IsNullOrEmpty(name)) continue;
                
                var isDir = hrefValue.EndsWith("/");
                
                DateTime? lastModified = null;
                var lastModifiedElement = responseElement.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "getlastmodified" || e.Name == XName.Get("getlastmodified", davNs.NamespaceName));
                if (lastModifiedElement != null && DateTime.TryParse(lastModifiedElement.Value, out var lm))
                {
                    lastModified = lm;
                }
                
                long size = 0;
                var sizeElement = responseElement.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "getcontentlength" || e.Name == XName.Get("getcontentlength", davNs.NamespaceName));
                if (sizeElement != null && long.TryParse(sizeElement.Value, out var s))
                {
                    size = s;
                }
                
                var fullPath = itemPath;
                if (!string.IsNullOrEmpty(path))
                {
                    fullPath = path.TrimStart('/') + "/" + name;
                }
                
                files.Add(new WebDAVFileInfo
                {
                    Name = name,
                    FullPath = fullPath,
                    Size = size,
                    LastModified = lastModified,
                    IsDirectory = isDir
                });
            }
            
            Log.Information("WebDAV 文件列表获取成功: {Count} 个项目, 路径: {Path}", files.Count, path ?? "根目录");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebDAV 文件列表获取异常");
        }
        
        return files;
    }
    
    public async Task<bool> DeleteFileAsync(string url, string username, string password, string fileName)
    {
        try
        {
            var fullUrl = url.TrimEnd('/') + "/" + fileName;
            var request = new HttpRequestMessage(HttpMethod.Delete, fullUrl);
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Log.Information("WebDAV 文件删除成功: {FileName}", fileName);
                return true;
            }
            
            Log.Warning("WebDAV 文件删除失败: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebDAV 文件删除异常: {FileName}", fileName);
            return false;
        }
    }
    
    public async Task<bool> CreateFolderAsync(string url, string username, string password, string folderName)
    {
        try
        {
            var fullUrl = url.TrimEnd('/') + "/" + folderName.TrimEnd('/') + "/";
            var request = new HttpRequestMessage(HttpMethod.Put, fullUrl);
            request.Content = new StringContent("");
            AddAuthentication(request, username, password);
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                Log.Information("WebDAV 文件夹创建成功: {FolderName}", folderName);
                return true;
            }
            
            Log.Warning("WebDAV 文件夹创建失败: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebDAV 文件夹创建异常: {FolderName}", folderName);
            return false;
        }
    }
    
    private void AddAuthentication(HttpRequestMessage request, string username, string password)
    {
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }
    
    private void OnProgressChanged(string fileName, int progress, string status, bool isUpload)
    {
        ProgressChanged?.Invoke(this, new WebDAVProgressEventArgs
        {
            FileName = fileName,
            Progress = progress,
            Status = status,
            IsUpload = isUpload
        });
    }
}
