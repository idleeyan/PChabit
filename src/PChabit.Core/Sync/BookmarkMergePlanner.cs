namespace PChabit.Core.Sync;

/// <summary>
/// 书签三路合并纯函数（与源扩展 v0.7 planThreeWayMerge 对齐）。
/// DB 是 source of truth；首次无基线只做 union，禁止传播删除。
/// 文件夹永不自动删除；改名只对 bookmark 生效。
/// </summary>
public static class BookmarkMergePlanner
{
    private static readonly string[] SkipUrlPrefixes =
    {
        "javascript:", "chrome://", "chrome-extension://", "edge://",
        "about:", "devtools://", "view-source:", "resource:", "edge-untrusted:"
    };

    public static bool ShouldSkipUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var lower = url.Trim().ToLowerInvariant();
        foreach (var p in SkipUrlPrefixes)
        {
            if (lower.StartsWith(p, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>origin + path(去尾斜杠) + search；丢弃 hash。非法 URL 返回 trim 原文。</summary>
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url?.Trim() ?? string.Empty;

        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            var path = u.AbsolutePath.TrimEnd('/');
            return $"{u.GetLeftPart(UriPartial.Authority)}{path}{u.Query}";
        }

        return url.Trim();
    }

    public static string ItemKey(FlatItem item)
    {
        if (item == null) return string.Empty;

        if (item.Url != null)
            return "b:" + NormalizeUrl(item.Url);

        var segments = new List<string>(item.Path ?? Array.Empty<string>()) { item.Title ?? string.Empty };
        return "f:" + string.Join("/", segments);
    }

    public static string EnsureKey(FlatItem item)
    {
        if (string.IsNullOrEmpty(item.Key))
            item.Key = ItemKey(item);
        return item.Key;
    }

    private static Dictionary<string, FlatItem> IndexByKey(IEnumerable<FlatItem> items)
    {
        var map = new Dictionary<string, FlatItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null) continue;
            var key = EnsureKey(item);
            if (string.IsNullOrEmpty(key)) continue;
            // 同 key 多条时保留 DateModified 较新者，避免改名被旧导出覆盖
            if (map.TryGetValue(key, out var prev) && prev.DateModified >= item.DateModified)
                continue;
            map[key] = item;
        }
        return map;
    }

    /// <summary>
    /// 对每个 key ∈ keys(local) ∪ keys(cloud) ∪ keys(last) 决策。
    /// last 为 null 或空 = 首次同步（纯 union，不删）。
    /// </summary>
    public static MergePlan PlanThreeWayMerge(
        IReadOnlyList<FlatItem> local,
        IReadOnlyList<FlatItem> cloud,
        IReadOnlyList<FlatItem>? last)
    {
        local ??= Array.Empty<FlatItem>();
        cloud ??= Array.Empty<FlatItem>();

        var localMap = IndexByKey(local);
        var cloudMap = IndexByKey(cloud);
        var hasHistory = last is { Count: > 0 };
        var lastMap = hasHistory ? IndexByKey(last!) : new Dictionary<string, FlatItem>();

        // 【数据安全】云端快照为空但存在同步基线且本地有数据 → 视为云端下载失败/文件丢失，
        // 绝不能走「基线有、云端无 = 他端删除」——否则会向所有浏览器批量删书签（2026-09-20 事故）。
        if (cloudMap.Count == 0 && lastMap.Count > 0 && localMap.Count > 0)
        {
            cloudMap = lastMap;
            hasHistory = false;
            lastMap = new Dictionary<string, FlatItem>(StringComparer.Ordinal);
        }

        // 安全模式说明：见上方 cloudMap 回填逻辑
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in localMap.Keys) keys.Add(k);
        foreach (var k in cloudMap.Keys) keys.Add(k);
        foreach (var k in lastMap.Keys) keys.Add(k);

        var plan = new MergePlan
        {
            // hasHistory 仅用于改名等提示；删除一律走浏览器显式检测
            HasHistory = hasHistory,
            Counts = new MergeCounts
            {
                Local = localMap.Count,
                Cloud = cloudMap.Count,
                Last = lastMap.Count
            }
        };

        foreach (var key in keys)
        {
            localMap.TryGetValue(key, out var l);
            cloudMap.TryGetValue(key, out var c);
            lastMap.TryGetValue(key, out var s);

            if (l != null && c != null)
            {
                // 双方都有 → 书签标题以 DateModified 较新者为准（改名同步的核心）
                var merged = l.Clone();
                merged.Key = key;

                if (l.Type == FlatItemType.Bookmark
                    && !string.Equals(l.Title, c.Title, StringComparison.Ordinal))
                {
                    // 云端更新 → 本机改名为云端
                    if (c.DateModified > l.DateModified)
                    {
                        plan.ToRenameLocal.Add(new RenameOp
                        {
                            Id = l.Id,
                            Title = c.Title,
                            Key = key
                        });
                        merged.Title = c.Title;
                        merged.DateModified = c.DateModified;
                        plan.Counts.RenameLocal++;
                    }
                    else
                    {
                        // 本机（多浏览器导出并集后）更新 → 权威标题为本机，上传云端；
                        // 写回时扩展按 URL 更新标题（见 applyPlan）
                        merged.Title = l.Title;
                        merged.DateModified = Math.Max(l.DateModified, c.DateModified);
                    }
                }
                else
                {
                    merged.Title = l.Type == FlatItemType.Bookmark && c.DateModified > l.DateModified
                        ? c.Title
                        : l.Title;
                    merged.DateModified = Math.Max(l.DateModified, c.DateModified);
                }

                plan.MergedCloud.Add(merged);
            }
            else if (l != null && c == null)
            {
                // 【安全】云端缺失 ≠ 删除。
                // WebDAV 空/残缺/半截快照时，若按「基线有、云端无 → 他端删除」会再次批量清空书签。
                // 删除只来自 DetectBrowserDeletions / DetectBrowserFolderDeletions 的显式路径。
                var merged = l.Clone();
                merged.Key = key;
                plan.MergedCloud.Add(merged);
            }
            else if (l == null && c != null)
            {
                // 云端新增（或首次）→ 写入本机 + 保留云端
                plan.ToAddLocal.Add(c.Clone());
                plan.MergedCloud.Add(c.Clone());
                plan.Counts.AddLocal++;
            }
            // l == null && c == null && s != null → 仅基线有，保留为合并结果（防丢）
            else if (l == null && c == null && s != null)
            {
                var merged = s.Clone();
                merged.Key = key;
                plan.MergedCloud.Add(merged);
            }
        }

        plan.Counts.Merged = plan.MergedCloud.Count;
        return plan;
    }

    /// <summary>
    /// 浏览器侧删除检测：对比「每浏览器上次导出快照」与「本次导出」，
    /// 返回「上次有、本次缺失」的 URL 书签 key（判定为该浏览器删除，可全局传播）。
    /// 安全规则：
    ///   - 首次（该浏览器无快照）不判删除；
    ///   - 文件夹永不删除；跳过特殊 URL（chrome:// 等内部页）；
    ///   - 本次未导出的浏览器不参与检测。
    /// </summary>
    /// <summary>
    /// 导出可信度：本次条数相对上次骤降时，不把缺失当作删除。
    /// 防止 Edge 等扩展导出中断/混批导致误删全局书签。
    /// 规则：上次 ≥20 条且本次 &lt; 上次的 50% → 不可信。
    /// </summary>
    public static bool IsExportReliableForDeletions(int currentCount, int lastCount)
    {
        if (lastCount <= 0) return true;
        if (lastCount < 20) return true;
        return currentCount >= lastCount * 0.5;
    }

    public static HashSet<string> DetectBrowserDeletions(
        IReadOnlyDictionary<string, List<FlatItem>>? currentExports,
        IReadOnlyDictionary<string, List<FlatItem>>? lastExports)
    {
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        if (currentExports is not { Count: > 0 } || lastExports is not { Count: > 0 })
            return deleted;

        foreach (var (browser, items) in currentExports)
        {
            if (!lastExports.TryGetValue(browser, out var prev) || prev is not { Count: > 0 })
                continue; // 首次见该浏览器 → 建立快照但不判删除

            if (!IsExportReliableForDeletions(items?.Count ?? 0, prev.Count))
                continue; // 本次导出异常偏少，跳过删除检测，避免误删

            // 上次快照中的 URL 书签索引
            var prevIndex = new Dictionary<string, FlatItem>(StringComparer.Ordinal);
            foreach (var p in prev)
            {
                if (p.Type != FlatItemType.Bookmark || string.IsNullOrEmpty(p.Url)) continue;
                var k = EnsureKey(p);
                if (!string.IsNullOrEmpty(k)) prevIndex[k] = p;
            }
            if (prevIndex.Count == 0) continue;

            var curKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in items)
            {
                var k = EnsureKey(i);
                if (!string.IsNullOrEmpty(k)) curKeys.Add(k);
            }

            foreach (var (key, item) in prevIndex)
            {
                if (curKeys.Contains(key)) continue;           // 本次仍在 → 未删除
                if (ShouldSkipUrl(item.Url)) continue;         // 内部页不参与删除
                deleted.Add(key);
            }
        }

        return deleted;
    }

    /// <summary>文件夹完整路径 = Path + Title。</summary>
    public static List<string> FullFolderPath(FlatItem folder)
    {
        var path = new List<string>(folder.Path ?? Array.Empty<string>());
        if (!string.IsNullOrEmpty(folder.Title))
            path.Add(folder.Title);
        return path;
    }

    /// <summary>根目录别名统一：Edge 收藏夹栏 = Chrome 书签栏。</summary>
    public static string CanonicalSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "";
        var s = segment.Trim();
        var lower = s.ToLowerInvariant();
        if (lower is "书签栏" or "收藏夹栏" or "bookmarks bar" or "bookmarks" or "favorites bar" or "favorites" or "收藏夹")
            return "书签栏";
        if (lower is "其他书签" or "其他收藏夹" or "other bookmarks" or "other" or "other favorites")
            return "其他书签";
        if (lower is "移动设备书签" or "移动收藏夹" or "移动设备收藏夹" or "mobile bookmarks" or "mobile favorites")
            return "移动设备书签";
        return s;
    }

    /// <summary>把「书签栏 / 工具」「书签栏/工具」拆成规范路径段。</summary>
    public static List<string> SplitPathString(string? pathOrTitle)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(pathOrTitle)) return result;
        var parts = pathOrTitle.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            var seg = raw.Trim();
            if (seg.Length == 0) continue;
            result.Add(CanonicalSegment(seg));
        }
        return result;
    }

    public static List<string> CanonicalizePath(IEnumerable<string>? path)
    {
        var list = new List<string>();
        if (path == null) return list;
        foreach (var raw in path)
            list.AddRange(SplitPathString(raw));
        var dedup = new List<string>();
        foreach (var s in list)
            if (dedup.Count == 0 || !string.Equals(dedup[^1], s, StringComparison.OrdinalIgnoreCase))
                dedup.Add(s);
        return dedup;
    }

    public static List<string> CanonicalFullPath(string type, string? title, IEnumerable<string>? path)
    {
        var p = CanonicalizePath(path);
        if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
        {
            var segs = SplitPathString(title);
            foreach (var s in segs)
                if (p.Count == 0 || !string.Equals(p[^1], s, StringComparison.OrdinalIgnoreCase))
                    p.Add(s);
        }
        return p;
    }

    public static List<string> CanonicalFolderPath(FlatItem folder)
        => CanonicalFullPath(folder.Type == FlatItemType.Folder ? "folder" : "bookmark", folder.Title, folder.Path);

    public static List<string> CanonicalBookmarkPath(IReadOnlyList<string>? path)
        => CanonicalizePath(path);

    public static bool IsUnderCanonical(IReadOnlyList<string> path, IReadOnlyList<string> folderFullPath)
    {
        if (path == null || folderFullPath == null || folderFullPath.Count == 0) return false;
        if (path.Count < folderFullPath.Count) return false;
        for (var i = 0; i < folderFullPath.Count; i++)
            if (!string.Equals(path[i], folderFullPath[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    public static bool IsUnderFolder(IReadOnlyList<string>? path, IReadOnlyList<string> folderFullPath)
        => IsUnderCanonical(CanonicalizePath(path), CanonicalizePath(folderFullPath));

    /// <summary>
    /// 浏览器侧文件夹删除检测：上次导出有、本次没有，且本次导出中已无任何书签/子文件夹落在该路径下。
    /// 导出不可靠时跳过；返回文件夹 key（f:path/title）。
    /// </summary>
    public static HashSet<string> DetectBrowserFolderDeletions(
        IReadOnlyDictionary<string, List<FlatItem>>? currentExports,
        IReadOnlyDictionary<string, List<FlatItem>>? lastExports)
    {
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        if (currentExports is not { Count: > 0 } || lastExports is not { Count: > 0 })
            return deleted;

        foreach (var (browser, items) in currentExports)
        {
            if (!lastExports.TryGetValue(browser, out var prev) || prev is not { Count: > 0 })
                continue;
            if (!IsExportReliableForDeletions(items?.Count ?? 0, prev.Count))
                continue;

            var cur = items ?? new List<FlatItem>();
            var curKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in cur)
            {
                var k = EnsureKey(i);
                if (!string.IsNullOrEmpty(k)) curKeys.Add(k);
            }

            foreach (var p in prev)
            {
                if (p.Type != FlatItemType.Folder) continue;
                var key = EnsureKey(p);
                if (string.IsNullOrEmpty(key) || curKeys.Contains(key)) continue;

                var full = FullFolderPath(p);
                var stillUsed = cur.Any(c =>
                    (c.Type == FlatItemType.Bookmark && IsUnderFolder(c.Path, full))
                    || (c.Type == FlatItemType.Folder && IsUnderFolder(FullFolderPath(c), full)));
                if (!stillUsed)
                    deleted.Add(key);
            }
        }

        return deleted;
    }

    /// <summary>
    /// 剔除孤儿文件夹：已无任何书签（含子文件夹下的书签）引用的文件夹。
    /// 解决「Edge 删除文件夹 → 书签删除传播后文件夹仍被写回重建」。
    /// </summary>
    public static List<FlatItem> PruneOrphanFolders(IReadOnlyList<FlatItem> items)
    {
        var list = items?.ToList() ?? new List<FlatItem>();
        var bookmarks = list.Where(i => i.Type == FlatItemType.Bookmark && !string.IsNullOrEmpty(i.Url)).ToList();

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var f = list[i];
                if (f.Type != FlatItemType.Folder) continue;
                var full = FullFolderPath(f);

                var hasBookmarkUnder = bookmarks.Any(b => IsUnderFolder(b.Path, full));
                if (hasBookmarkUnder) continue;

                var hasChildFolderWithContent = false;
                foreach (var g in list)
                {
                    if (g.Type != FlatItemType.Folder || ReferenceEquals(g, f)) continue;
                    var gFull = FullFolderPath(g);
                    if (gFull.Count <= full.Count) continue;
                    if (!IsUnderFolder(gFull, full)) continue;
                    if (bookmarks.Any(b => IsUnderFolder(b.Path, gFull)))
                    {
                        hasChildFolderWithContent = true;
                        break;
                    }
                }

                if (!hasChildFolderWithContent)
                {
                    list.RemoveAt(i);
                    changed = true;
                }
            }
        }

        return list;
    }

    /// <summary>合并后应从本地删除的文件夹：本地仍存在、合并权威集合中已无（孤儿或已判定删除）。</summary>
    public static List<FlatItem> ComputeFoldersToRemove(
        IReadOnlyList<FlatItem> localItems,
        IReadOnlyList<FlatItem> mergedCloud,
        IEnumerable<string>? explicitlyDeletedKeys = null)
    {
        var mergedFolderKeys = new HashSet<string>(StringComparer.Ordinal);
        var mergedBookmarkPaths = new List<List<string>>();
        foreach (var m in mergedCloud)
        {
            if (m.Type == FlatItemType.Folder)
                mergedFolderKeys.Add(string.Join("|", CanonicalFolderPath(m)));
            else if (m.Type == FlatItemType.Bookmark)
                mergedBookmarkPaths.Add(CanonicalBookmarkPath(m.Path));
        }

        var explicitKeys = explicitlyDeletedKeys is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(explicitlyDeletedKeys, StringComparer.Ordinal);

        var result = new List<FlatItem>();
        foreach (var local in localItems)
        {
            if (local.Type != FlatItemType.Folder) continue;
            var key = EnsureKey(local);
            if (string.IsNullOrEmpty(key)) continue;

            var canonical = CanonicalFolderPath(local);
            var canonKey = string.Join("|", canonical);
            if (mergedFolderKeys.Contains(canonKey)) continue;
            if (mergedBookmarkPaths.Any(bp => IsUnderCanonical(bp, canonical))) continue;

            if (explicitKeys.Contains(key))
                result.Add(local);
        }
        return result;
    }
}
