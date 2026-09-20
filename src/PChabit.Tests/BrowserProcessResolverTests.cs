using PChabit.Infrastructure.Services;
using Xunit;

namespace PChabit.Tests;

/// <summary>
/// 浏览器识别两级证据链：exe 路径（安装目录品牌）→ 进程名兜底。
/// 不依赖 UA / 内核版本号（版本会更新，Chromium 系 UA 几乎相同）。
/// </summary>
public class BrowserProcessResolverTests
{
    [Theory]
    [InlineData(@"D:\Tool\Doubao\app\Doubao.exe", "Doubao", "豆包浏览器")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "msedge", "Edge")]
    [InlineData(@"C:\Program Files\Microsoft\Edge\Application\msedge.exe", "msedge", "Edge")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome", "Chrome")]
    [InlineData(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe", "chrome", "Chrome")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox", "Firefox")]
    public void ResolveBrowserName_Should_ResolveByExePath(string exe, string proc, string expected)
    {
        Assert.Equal(expected, BrowserProcessResolver.ResolveBrowserName(exe, proc));
    }

    [Theory]
    [InlineData(null, "chrome", "Chrome")]
    [InlineData(null, "msedge", "Edge")]
    [InlineData(null, "Doubao", "豆包浏览器")]
    [InlineData(null, "firefox", "Firefox")]
    [InlineData(null, "msedgewebview2", "Edge")]
    public void ResolveBrowserName_Should_FallbackToProcessName(string? exe, string proc, string expected)
    {
        Assert.Equal(expected, BrowserProcessResolver.ResolveBrowserName(exe, proc));
    }

    [Theory]
    [InlineData(null, null, "未知浏览器")]
    [InlineData(null, "totally_unknown_proc", "totally_unknown_proc")]
    [InlineData(@"C:\Some\Other\vendor.exe", null, "未知浏览器")]
    public void ResolveBrowserName_Should_ReportUnknownWhenNoEvidence(string? exe, string? proc, string expected)
    {
        Assert.Equal(expected, BrowserProcessResolver.ResolveBrowserName(exe, proc));
    }

    [Theory]
    // 品牌目录优先于进程名（防止第三方程错误用 chrome.exe 命名被认成 Chrome）
    [InlineData(@"D:\ThirdParty\Doubao\app\chrome.exe", "chrome", "豆包浏览器")]
    // 路径不认识时按进程名兜底
    [InlineData(@"D:\Custom\dir\chrome.exe", "chrome", "Chrome")]
    [InlineData(@"D:\Custom\dir\msedge.exe", "msedge", "Edge")]
    public void ResolveBrowserName_Should_PreferPathBrandOverProcessName(string exe, string proc, string expected)
    {
        Assert.Equal(expected, BrowserProcessResolver.ResolveBrowserName(exe, proc));
    }
}
