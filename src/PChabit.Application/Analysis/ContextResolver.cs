using System.Text.RegularExpressions;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Application.Analysis;

public class ContextResolver : IContextResolver
{
    private readonly IRepository<AppSession> _appSessionRepo;
    
    private static readonly Dictionary<string, ContextType> AppContextMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "code", ContextType.Development },
        { "devenv", ContextType.Development },
        { "idea64", ContextType.Development },
        { "pycharm64", ContextType.Development },
        { "webstorm64", ContextType.Development },
        { "rider64", ContextType.Development },
        { "visualstudio", ContextType.Development },
        { "vscode", ContextType.Development },
        { "sublime_text", ContextType.Development },
        { "notepad++", ContextType.Development },
        
        { "figma", ContextType.Design },
        { "xd", ContextType.Design },
        { "photoshop", ContextType.Design },
        { "illustrator", ContextType.Design },
        { "sketch", ContextType.Design },
        { "blender", ContextType.Design },
        
        { "word", ContextType.Writing },
        { "onenote", ContextType.Writing },
        { "obsidian", ContextType.Writing },
        { "notion", ContextType.Writing },
        
        { "slack", ContextType.Communication },
        { "teams", ContextType.Communication },
        { "discord", ContextType.Communication },
        { "zoom", ContextType.Communication },
        { "outlook", ContextType.Communication },
        { "telegram", ContextType.Communication },
        { "wechat", ContextType.Communication },
        
        { "chrome", ContextType.Research },
        { "msedge", ContextType.Research },
        { "firefox", ContextType.Research },
        
        { "youtube", ContextType.Entertainment },
        { "netflix", ContextType.Entertainment },
        { "spotify", ContextType.Entertainment },
        { "twitch", ContextType.Entertainment },
        
        { "twitter", ContextType.Social },
        { "facebook", ContextType.Social },
        { "instagram", ContextType.Social },
        { "reddit", ContextType.Social },
        
        { "excel", ContextType.Productivity },
        { "powerpnt", ContextType.Productivity },
        { "project", ContextType.Productivity },
        
        { "explorer", ContextType.System },
        { "settings", ContextType.System },
        { "taskmgr", ContextType.System }
    };
    
    private static readonly Dictionary<ContextType, double> ContextProductivity = new()
    {
        { ContextType.Development, 0.95 },
        { ContextType.Design, 0.9 },
        { ContextType.Writing, 0.85 },
        { ContextType.Productivity, 0.8 },
        { ContextType.Research, 0.6 },
        { ContextType.Communication, 0.5 },
        { ContextType.Social, 0.2 },
        { ContextType.Entertainment, 0.1 },
        { ContextType.System, 0.3 },
        { ContextType.Other, 0.5 }
    };
    
    private static readonly Dictionary<ContextType, string[]> ContextKeywords = new()
    {
        { ContextType.Development, new[] { "debug", "build", "compile", "git", "commit", "push", "pull", "merge", "branch", "code", "function", "class", "method" } },
        { ContextType.Writing, new[] { "document", "report", "article", "blog", "note", "draft", "edit" } },
        { ContextType.Research, new[] { "search", "wiki", "docs", "tutorial", "learn", "stack overflow", "github" } },
        { ContextType.Communication, new[] { "meeting", "call", "chat", "message", "email", "discussion" } },
        { ContextType.Entertainment, new[] { "watch", "play", "stream", "music", "video", "game" } }
    };
    
    private static readonly Regex ProjectPattern = new(@"\[([^\]]+)\]|[-–]\s*([^-–]+)$|project[:\s]+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TaskPattern = new(@"task[:\s]+(\w+)|todo[:\s]+(\w+)|fix[:\s]+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    public ContextResolver(IRepository<AppSession> appSessionRepo)
    {
        _appSessionRepo = appSessionRepo;
    }
    
    public async Task<ActivityContext> ResolveContextAsync(AppSession session)
    {
        return await ResolveContextAsync(session.ProcessName, session.WindowTitle, session.StartTime);
    }
    
    public async Task<ActivityContext> ResolveContextAsync(string processName, string? windowTitle, DateTime timestamp)
    {
        var context = new ActivityContext
        {
            Type = ResolveContextType(processName),
            Category = ResolveCategory(processName),
            ProductivityScore = ResolveProductivityScore(processName),
            FocusScore = ResolveFocusScore(processName)
        };
        
        if (!string.IsNullOrEmpty(windowTitle))
        {
            context.Project = ExtractProject(windowTitle);
            context.Task = ExtractTask(windowTitle);
            context.Tags = ExtractTags(windowTitle, context.Type);
            context.Description = CleanWindowTitle(windowTitle);
        }
        
        return context;
    }
    
    public async Task<List<ContextTag>> GetContextTagsAsync(DateTime startTime, DateTime endTime)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime >= startTime && s.StartTime < endTime);
        
        var tagCounts = new Dictionary<string, (int Count, TimeSpan Duration)>();
        
        foreach (var session in sessions)
        {
            var context = await ResolveContextAsync(session);
            
            foreach (var tag in context.Tags)
            {
                if (!tagCounts.ContainsKey(tag))
                {
                    tagCounts[tag] = (0, TimeSpan.Zero);
                }
                
                var existing = tagCounts[tag];
                tagCounts[tag] = (existing.Count + 1, existing.Duration + session.Duration);
            }
        }
        
        var totalDuration = sessions.Sum(s => s.Duration.TotalMinutes);
        
        return tagCounts
            .Select(kvp => new ContextTag
            {
                Name = kvp.Key,
                OccurrenceCount = kvp.Value.Count,
                TotalDuration = kvp.Value.Duration,
                RelevanceScore = totalDuration > 0 ? kvp.Value.Duration.TotalMinutes / totalDuration : 0
            })
            .OrderByDescending(t => t.TotalDuration)
            .Take(20)
            .ToList();
    }
    
    private ContextType ResolveContextType(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        
        foreach (var kvp in AppContextMap)
        {
            if (name.Contains(kvp.Key.ToLowerInvariant()))
            {
                return kvp.Value;
            }
        }
        
        return ContextType.Other;
    }
    
    private string ResolveCategory(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        
        if (name.Contains("code") || name.Contains("devenv") || name.Contains("idea") || name.Contains("vscode"))
            return "Development";
        
        if (name.Contains("figma") || name.Contains("xd") || name.Contains("photoshop"))
            return "Design";
        
        if (name.Contains("chrome") || name.Contains("edge") || name.Contains("firefox"))
            return "Browser";
        
        if (name.Contains("slack") || name.Contains("teams") || name.Contains("discord"))
            return "Communication";
        
        if (name.Contains("excel") || name.Contains("word") || name.Contains("powerpnt"))
            return "Office";
        
        if (name.Contains("youtube") || name.Contains("netflix") || name.Contains("spotify"))
            return "Entertainment";
        
        return "Other";
    }
    
    private double ResolveProductivityScore(string processName)
    {
        var contextType = ResolveContextType(processName);
        return ContextProductivity.GetValueOrDefault(contextType, 0.5);
    }
    
    private double ResolveFocusScore(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        
        var focusApps = new[] { "code", "devenv", "idea", "vscode", "figma", "xd", "word", "onenote", "obsidian" };
        var distractionApps = new[] { "youtube", "netflix", "spotify", "twitter", "facebook", "instagram" };
        
        if (focusApps.Any(a => name.Contains(a)))
            return 0.9;
        
        if (distractionApps.Any(a => name.Contains(a)))
            return 0.2;
        
        return 0.5;
    }
    
    private string? ExtractProject(string windowTitle)
    {
        var match = ProjectPattern.Match(windowTitle);
        if (match.Success)
        {
            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    return match.Groups[i].Value.Trim();
                }
            }
        }
        
        var separators = new[] { " - ", " – ", " | ", " :: " };
        var parts = windowTitle.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length >= 2)
        {
            return parts[^1].Trim();
        }
        
        return null;
    }
    
    private string? ExtractTask(string windowTitle)
    {
        var match = TaskPattern.Match(windowTitle);
        if (match.Success)
        {
            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    return match.Groups[i].Value.Trim();
                }
            }
        }
        
        return null;
    }
    
    private List<string> ExtractTags(string windowTitle, ContextType contextType)
    {
        var tags = new List<string>();
        var lowerTitle = windowTitle.ToLowerInvariant();
        
        if (ContextKeywords.TryGetValue(contextType, out var keywords))
        {
            foreach (var keyword in keywords)
            {
                if (lowerTitle.Contains(keyword))
                {
                    tags.Add(keyword);
                }
            }
        }
        
        if (lowerTitle.Contains("meeting") || lowerTitle.Contains("call"))
            tags.Add("meeting");
        
        if (lowerTitle.Contains("urgent") || lowerTitle.Contains("important"))
            tags.Add("priority");
        
        if (lowerTitle.Contains("review") || lowerTitle.Contains("pr"))
            tags.Add("review");
        
        if (lowerTitle.Contains("bug") || lowerTitle.Contains("fix"))
            tags.Add("bugfix");
        
        if (lowerTitle.Contains("feature") || lowerTitle.Contains("implement"))
            tags.Add("feature");
        
        return tags.Distinct().ToList();
    }
    
    private string CleanWindowTitle(string windowTitle)
    {
        var separators = new[] { " - ", " – ", " | " };
        var parts = windowTitle.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length > 0)
        {
            return parts[0].Trim();
        }
        
        return windowTitle;
    }
}
