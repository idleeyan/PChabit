using System.Diagnostics;
using Serilog;

namespace PChabit.App.Services;

public static class UrlLauncher
{
    /// <summary>用系统默认浏览器打开 URL/域名。非法输入不抛异常。</summary>
    public static bool TryOpen(string? urlOrDomain)
    {
        if (string.IsNullOrWhiteSpace(urlOrDomain))
            return false;

        var target = Normalize(urlOrDomain.Trim());
        if (target == null)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            Log.Information("已打开链接: {Url}", target);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开链接失败: {Url}", target);
            return false;
        }
    }

    public static string? Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var s = input.Trim();

        if (Uri.TryCreate(s, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.AbsoluteUri;
        }

        // 裸域名 / host:port
        if (Uri.TryCreate("https://" + s, UriKind.Absolute, out var https) &&
            https.Host.Contains('.'))
        {
            return https.AbsoluteUri;
        }

        return null;
    }
}
