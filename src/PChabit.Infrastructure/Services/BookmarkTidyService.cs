using System.Text.RegularExpressions;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.Sync;

namespace PChabit.Infrastructure.Services;

public class BookmarkTidyResult
{
    public int Scanned { get; set; }
    public int Renamed { get; set; }
    public int Skipped { get; set; }
    public List<(string OldTitle, string NewTitle, string Url)> Changes { get; set; } = new();
    public string Message { get; set; } = "";
}

/// <summary>
/// 智能整理书签标题：去尾缀、压缩空白、去过长省略号等。
/// 只改 title，不改 URL/路径；改完写回 DB，下次同步会推到各浏览器。
/// </summary>
public class BookmarkTidyService
{
    private readonly IBrowserBookmarkRepository _repository;

    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex EdgeEllipsis = new(@"[…]{1,3}\s*$", RegexOptions.Compiled);

    public BookmarkTidyService(IBrowserBookmarkRepository repository)
    {
        _repository = repository;
    }

    public static string CleanTitle(string? title, string? url = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return title?.Trim() ?? "";

        var t = title.Trim();

        // 去掉尾部省略号 / 半截标题
        t = EdgeEllipsis.Replace(t, "").Trim();
        t = Regex.Replace(t, @"\.\.\.\s*$", "").Trim();

        // 常见冗余后缀
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*(Google|百度|Bing|搜狗|必应)?\s*搜索\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*Google\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*(官网|官方网站|首页|主页)\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*Chrome\s*(网上应用店|Web Store)\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*Microsoft\s*Edge\s*(加载项|Addons)\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*(登录|注册|登入)\s*$", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*[\(（【\[]\s*(官网|官方|Official)\s*[\)）】\]]\s*$", "", RegexOptions.IgnoreCase).Trim();

        // 「标题 - 站点名」：站点名很短时砍掉，保留主体
        t = Regex.Replace(t, @"\s*[\|\-–—]\s*[^|\-–—]{1,10}\s*$", m =>
        {
            var head = t[..^m.Length].Trim();
            return head.Length >= 2 ? "" : m.Value;
        }).Trim();

        t = MultiSpace.Replace(t, " ").Trim();

        // 全空或只剩符号时回退原标题
        if (t.Length < 1 || t.All(c => !char.IsLetterOrDigit(c)))
            return title.Trim();

        if (t.Length > 80)
            t = t[..77].TrimEnd() + "…";

        return t;
    }

    public async Task<BookmarkTidyResult> TidyAsync(bool apply, CancellationToken ct = default)
    {
        var result = new BookmarkTidyResult();
        var items = await _repository.GetAllActiveAsync(ct);

        var toUpdate = new List<BrowserBookmark>();
        foreach (var bm in items)
        {
            if (bm.Type != BrowserBookmarkTypes.Bookmark) { result.Skipped++; continue; }

            var cleaned = CleanTitle(bm.Title, bm.Url);
            if (string.IsNullOrEmpty(cleaned) || cleaned == bm.Title)
            {
                result.Skipped++;
                continue;
            }

            result.Scanned++;
            result.Changes.Add((bm.Title, cleaned, bm.Url ?? ""));
            if (apply)
            {
                bm.Title = cleaned;
                toUpdate.Add(bm);
            }
        }

        result.Renamed = apply ? toUpdate.Count : result.Changes.Count;

        if (apply && toUpdate.Count > 0)
        {
            await _repository.UpsertManyAsync(toUpdate, ct);
            Log.Information("书签智能整理：改名 {N} 条", toUpdate.Count);
        }

        result.Message = apply
            ? $"已整理 {result.Renamed} 条标题（扫描 {items.Count}，跳过 {result.Skipped}）"
            : $"预览：{result.Changes.Count} 条可优化（扫描 {items.Count}）";

        return result;
    }
}
