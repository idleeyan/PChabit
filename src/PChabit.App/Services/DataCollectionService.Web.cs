using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;

namespace PChabit.App.Services;

/// <summary>单个网页会话的运行时活跃跟踪状态</summary>
internal sealed class WebSessionRuntime
{
    public DateTime LastActiveAt { get; set; }
    public TimeSpan AccumulatedActive { get; set; } = TimeSpan.Zero;
    public bool IsCurrentlyActive { get; set; } = true;
    public bool IsIdle { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsForegroundTab { get; set; } = true;
    public DateTime LastSeenAt { get; set; }
}

public partial class DataCollectionService : IDisposable
{
    private readonly Dictionary<string, WebSessionRuntime> _webRuntime = new();

    private void OnWebActivityReceived(object? sender, WebActivityEventArgs e)
    {
        var sessionKey = $"{e.ClientId}_{e.TabId}";

        switch (e.ActivityType)
        {
            case WebActivityType.PageView:
                HandlePageView(e, sessionKey);
                break;

            case WebActivityType.TabSwitch:
                HandleTabSwitch(e, sessionKey);
                break;

            case WebActivityType.PageClose:
            case WebActivityType.TabClose:
                HandlePageClose(sessionKey);
                break;

            case WebActivityType.Scroll:
                HandleScroll(e, sessionKey);
                break;

            case WebActivityType.Click:
                HandleClick(e, sessionKey);
                break;

            case WebActivityType.FormSubmit:
                HandleFormSubmit(e, sessionKey);
                break;

            case WebActivityType.Search:
                HandleSearch(e, sessionKey);
                break;

            case WebActivityType.Heartbeat:
                HandleHeartbeat(e, sessionKey);
                break;

            case WebActivityType.Visibility:
                HandleVisibility(e, sessionKey);
                break;

            case WebActivityType.Idle:
                HandleIdle(e, sessionKey);
                break;
        }
    }

    private void HandlePageView(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            CloseActiveSessionLocked(sessionKey);

            var timestamp = NormalizeTimestamp(e.Timestamp);
            var session = CreateSession(e, sessionKey, timestamp);
            _activeWebSessions[sessionKey] = session;
            _webRuntime[sessionKey] = CreateRuntime(timestamp, e);
        }
    }

    private void HandleTabSwitch(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            // 同 tab 同 URL：仅恢复前台，不新建会话
            if (_activeWebSessions.TryGetValue(sessionKey, out var existing)
                && !string.IsNullOrEmpty(e.Url)
                && string.Equals(existing.Url, e.Url, StringComparison.OrdinalIgnoreCase))
            {
                existing.IsActiveTab = true;
                if (_webRuntime.TryGetValue(sessionKey, out var rt))
                {
                    rt.IsForegroundTab = true;
                    ResumeActiveLocked(rt, e.Timestamp);
                }
                else
                {
                    _webRuntime[sessionKey] = CreateRuntime(existing.StartTime, e);
                }
                return;
            }

            CloseActiveSessionLocked(sessionKey);

            var timestamp = NormalizeTimestamp(e.Timestamp);
            var session = CreateSession(e, sessionKey, timestamp);
            _activeWebSessions[sessionKey] = session;
            _webRuntime[sessionKey] = CreateRuntime(timestamp, e);
        }
    }

    private static DateTime NormalizeTimestamp(DateTime timestamp)
    {
        if (timestamp.Kind == DateTimeKind.Utc)
        {
            return timestamp.ToLocalTime();
        }

        return timestamp == default ? DateTime.Now : timestamp;
    }

    private WebSession CreateSession(WebActivityEventArgs e, string sessionKey, DateTime timestamp)
    {
        var session = new WebSession
        {
            Id = Guid.NewGuid(),
            Url = e.Url,
            Title = e.Title,
            Domain = e.Domain,
            Browser = e.Browser,
            TabId = e.TabId,
            StartTime = timestamp,
            Favicon = e.Favicon,
            ScrollDepth = 0,
            ClickCount = 0,
            HasFormInteraction = false,
            IsActiveTab = true,
            ActiveDuration = TimeSpan.Zero,
            IdleDuration = TimeSpan.Zero
        };

        MaterializeCategoryLocked(session);
        return session;
    }

    private static WebSessionRuntime CreateRuntime(DateTime start, WebActivityEventArgs e)
    {
        return new WebSessionRuntime
        {
            LastActiveAt = start,
            LastSeenAt = start,
            IsCurrentlyActive = e.IsIdle != true,
            IsIdle = e.IsIdle == true,
            IsVisible = e.IsVisible ?? true,
            IsForegroundTab = true
        };
    }

    /// <summary>把当前墙钟区间累入 ActiveDuration（若此前处于活跃）</summary>
    private static void AccrueActiveLocked(WebSessionRuntime rt, DateTime now)
    {
        if (!rt.IsCurrentlyActive) return;

        var delta = now - rt.LastActiveAt;
        if (delta > TimeSpan.Zero)
        {
            rt.AccumulatedActive += delta;
        }
        rt.LastActiveAt = now;
    }

    private static void ResumeActiveLocked(WebSessionRuntime rt, DateTime eventTime)
    {
        var now = eventTime == default ? DateTime.Now : eventTime;
        if (now.Kind == DateTimeKind.Utc) now = now.ToLocalTime();

        rt.LastActiveAt = now;
        rt.LastSeenAt = now;
        rt.IsCurrentlyActive = !rt.IsIdle && rt.IsVisible && rt.IsForegroundTab;
    }

    private void CloseActiveSessionLocked(string sessionKey)
    {
        if (!_activeWebSessions.TryGetValue(sessionKey, out var session)) return;

        var now = DateTime.Now;
        FinalizeSessionLocked(sessionKey, session, now);
        EnqueueSaveWebSession(session);

        _activeWebSessions.Remove(sessionKey);
        _webRuntime.Remove(sessionKey);
    }

    private void FinalizeSessionLocked(string sessionKey, WebSession session, DateTime now)
    {
        if (_webRuntime.TryGetValue(sessionKey, out var rt))
        {
            AccrueActiveLocked(rt, now);
            session.ActiveDuration = rt.AccumulatedActive;
            var wall = session.EndTime.HasValue ? session.EndTime.Value - session.StartTime : now - session.StartTime;
            session.IdleDuration = wall > session.ActiveDuration ? wall - session.ActiveDuration : TimeSpan.Zero;
        }

        session.EndTime = now;
        session.Duration = now - session.StartTime;
        session.IsActiveTab = false;

        if (session.ActiveDuration > session.Duration)
        {
            session.ActiveDuration = session.Duration;
        }
    }

    private void HandlePageClose(string sessionKey)
    {
        lock (_lock)
        {
            CloseActiveSessionLocked(sessionKey);
        }
    }

    private void HandleHeartbeat(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (!_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                // 无会话时，若带 URL 则补建（扩展重启等场景）
                if (!string.IsNullOrEmpty(e.Url))
                {
                    var timestamp = NormalizeTimestamp(e.Timestamp);
                    session = CreateSession(e, sessionKey, timestamp);
                    _activeWebSessions[sessionKey] = session;
                    _webRuntime[sessionKey] = CreateRuntime(timestamp, e);
                }
                else
                {
                    return;
                }
            }

            if (!_webRuntime.TryGetValue(sessionKey, out var rt))
            {
                rt = CreateRuntime(session.StartTime, e);
                _webRuntime[sessionKey] = rt;
            }

            var now = NormalizeTimestamp(e.Timestamp);
            var idle = e.IsIdle ?? (e.IsActive == false);
            var visible = e.IsVisible ?? e.IsActive ?? true;

            ApplyActivityStateLocked(session, rt, now, idle, visible, isForeground: true);
        }
    }

    private void HandleVisibility(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (!_activeWebSessions.TryGetValue(sessionKey, out var session)) return;
            if (!_webRuntime.TryGetValue(sessionKey, out var rt)) return;

            var now = NormalizeTimestamp(e.Timestamp);
            var visible = e.IsVisible ?? true;
            ApplyActivityStateLocked(session, rt, now, rt.IsIdle, visible, rt.IsForegroundTab);
        }
    }

    private void HandleIdle(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (!_activeWebSessions.TryGetValue(sessionKey, out var session)) return;
            if (!_webRuntime.TryGetValue(sessionKey, out var rt)) return;

            var now = NormalizeTimestamp(e.Timestamp);
            var idle = e.IsIdle ?? true;
            ApplyActivityStateLocked(session, rt, now, idle, rt.IsVisible, rt.IsForegroundTab);
        }
    }

    private void ApplyActivityStateLocked(
        WebSession session,
        WebSessionRuntime rt,
        DateTime now,
        bool idle,
        bool visible,
        bool isForeground)
    {
        var shouldBeActive = !idle && visible && isForeground;

        if (rt.IsCurrentlyActive)
        {
            AccrueActiveLocked(rt, now);
        }

        rt.IsIdle = idle;
        rt.IsVisible = visible;
        rt.IsForegroundTab = isForeground;
        rt.LastSeenAt = now;
        rt.IsCurrentlyActive = shouldBeActive;

        if (shouldBeActive)
        {
            rt.LastActiveAt = now;
        }

        session.IsActiveTab = isForeground;
        session.ActiveDuration = rt.AccumulatedActive;
    }

    private void HandleScroll(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                if (e.Metadata.TryGetValue("percentage", out var percentageObj) && percentageObj is int percentage)
                {
                    session.ScrollDepth = Math.Max(session.ScrollDepth, percentage);
                }

                if (_webRuntime.TryGetValue(sessionKey, out var rt))
                {
                    var now = NormalizeTimestamp(e.Timestamp);
                    if (!rt.IsCurrentlyActive)
                    {
                        rt.IsIdle = false;
                        ResumeActiveLocked(rt, now);
                    }
                    else
                    {
                        rt.LastSeenAt = now;
                        rt.LastActiveAt = now;
                    }
                }
            }
        }
    }

    private void HandleClick(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                session.ClickCount++;

                if (e.Metadata.TryGetValue("element", out var elementObj) && elementObj is Dictionary<string, object> element)
                {
                    if (element.TryGetValue("tag", out var tagObj) && tagObj is string tag)
                    {
                        session.InteractedElements.Add($"{tag}:{DateTime.Now:HH:mm:ss}");
                    }
                }

                if (_webRuntime.TryGetValue(sessionKey, out var rt))
                {
                    var now = NormalizeTimestamp(e.Timestamp);
                    if (!rt.IsCurrentlyActive)
                    {
                        rt.IsIdle = false;
                        ResumeActiveLocked(rt, now);
                    }
                    else
                    {
                        rt.LastSeenAt = now;
                        rt.LastActiveAt = now;
                    }
                }
            }
        }
    }

    private void HandleFormSubmit(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                session.HasFormInteraction = true;
            }
        }
    }

    private void HandleSearch(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                if (e.Metadata.TryGetValue("query", out var queryObj) && queryObj is string query)
                {
                    session.SearchQueries.Add(query);
                }
            }
        }
    }

    /// <summary>落库时物化分类（Mapping 优先，关键词 fallback 次之）</summary>
    private void MaterializeCategoryLocked(WebSession session)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetService<IWebsiteCategoryService>();
            if (categoryService == null)
            {
                session.CategoryName = "浏览";
                session.CategorySource = "Unknown";
                return;
            }

            var mapping = categoryService.GetAllMappingsSync()
                .FirstOrDefault(m => DomainMatches(session.Domain, m.DomainPattern));

            if (mapping != null)
            {
                var cat = categoryService.GetAllCategoriesSync().FirstOrDefault(c => c.Id == mapping.CategoryId);
                if (cat != null)
                {
                    session.CategoryId = cat.Id;
                    session.CategoryName = cat.Name;
                    session.CategorySource = "Mapping";
                    return;
                }
            }

            var fallbackName = FallbackCategoryName(session.Domain);
            var fallbackCat = categoryService.GetAllCategoriesSync()
                .FirstOrDefault(c => c.Name == fallbackName);
            if (fallbackCat != null)
            {
                session.CategoryId = fallbackCat.Id;
                session.CategoryName = fallbackCat.Name;
                session.CategorySource = "Fallback";
            }
            else
            {
                session.CategoryName = fallbackName;
                session.CategorySource = "Fallback";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "物化网站分类失败: {Domain}", session.Domain);
            session.CategoryName = "浏览";
            session.CategorySource = "Unknown";
        }
    }

    private static bool DomainMatches(string domain, string pattern)
    {
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(pattern)) return false;

        var lowerDomain = domain.ToLowerInvariant();
        var lowerPattern = pattern.ToLowerInvariant();

        if (lowerPattern.StartsWith("*."))
        {
            var suffix = lowerPattern[2..];
            return lowerDomain.EndsWith("." + suffix) || lowerDomain == suffix;
        }

        return lowerDomain == lowerPattern || lowerDomain.EndsWith("." + lowerPattern);
    }

    /// <summary>与 WebsiteCategoryService.GetDefaultCategory 对齐的单一 fallback 逻辑</summary>
    private static string FallbackCategoryName(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";

        var lowerDomain = domain.ToLowerInvariant();

        if (lowerDomain.Contains("google") || lowerDomain.Contains("baidu") ||
            lowerDomain.Contains("bing") || lowerDomain.Contains("sogou"))
            return "搜索";

        if (lowerDomain.Contains("github") || lowerDomain.Contains("gitlab") ||
            lowerDomain.Contains("stackoverflow") || lowerDomain.Contains("csdn") ||
            lowerDomain.Contains("juejin") || lowerDomain.Contains("segmentfault"))
            return "开发";

        if (lowerDomain.Contains("youtube") || lowerDomain.Contains("bilibili") ||
            lowerDomain.Contains("netflix") || lowerDomain.Contains("youku") ||
            lowerDomain.Contains("iqiyi") || lowerDomain.Contains("douyin"))
            return "视频";

        if (lowerDomain.Contains("twitter") || lowerDomain.Contains("weibo") ||
            lowerDomain.Contains("facebook") || lowerDomain.Contains("instagram") ||
            lowerDomain.Contains("linkedin") || lowerDomain.Contains("zhihu") ||
            lowerDomain.Contains("xiaohongshu") || lowerDomain.Contains("douban"))
            return "社交";

        if (lowerDomain.Contains("amazon") || lowerDomain.Contains("taobao") ||
            lowerDomain.Contains("jd") || lowerDomain.Contains("tmall") ||
            lowerDomain.Contains("pinduoduo"))
            return "购物";

        if (lowerDomain.Contains("mail") || lowerDomain.Contains("outlook") ||
            lowerDomain.Contains("gmail"))
            return "邮件";

        if (lowerDomain.Contains("notion") || lowerDomain.Contains("docs.qq") ||
            lowerDomain.Contains("yuque") || lowerDomain.Contains("confluence") ||
            lowerDomain.Contains("feishu") || lowerDomain.Contains("dingtalk"))
            return "办公";

        if (lowerDomain.Contains("news") || lowerDomain.Contains("bbc") ||
            lowerDomain.Contains("cnn") || lowerDomain.Contains("sina") ||
            lowerDomain.Contains("sohu") || lowerDomain.Contains("163"))
            return "新闻";

        return "浏览";
    }

    private void OnWebClientDisconnected(object? sender, WebClientDisconnectedEventArgs e)
    {
        Log.Information("浏览器断开连接，保存相关网页会话: {ClientId}", e.ClientId);

        lock (_lock)
        {
            var keysToSave = _activeWebSessions.Keys
                .Where(k => k.StartsWith(e.ClientId))
                .ToList();

            foreach (var key in keysToSave)
            {
                CloseActiveSessionLocked(key);
            }
        }
    }
}
