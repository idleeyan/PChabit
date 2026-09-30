using PChabit.Infrastructure.Formatters;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// AI 发现与规则洞察去重：AI 只保留规则没说过的增量观点。
/// 规则优先（本地确定性结果优先于模型措辞）。
/// </summary>
public static class AiInsightDeduper
{
    /// <summary>过滤掉与规则洞察标题/正文高度重叠的 AI finding。</summary>
    public static List<AnalyticsAiResponseParser.AiFinding> FilterFindings(
        IReadOnlyList<AnalyticsAiResponseParser.AiFinding> findings,
        IReadOnlyList<(string Title, string Message)> ruleInsights)
    {
        var result = new List<AnalyticsAiResponseParser.AiFinding>();
        foreach (var f in findings)
        {
            if (IsDuplicate(f.Title, f.Detail, ruleInsights))
                continue;
            result.Add(f);
        }
        return result;
    }

    public static bool IsDuplicate(
        string title,
        string detail,
        IReadOnlyList<(string Title, string Message)> ruleInsights)
    {
        var hay = Normalize(title + " " + detail);
        if (hay.Length < 4) return false;
        foreach (var (rTitle, rMsg) in ruleInsights)
        {
            var needle = Normalize(rTitle + " " + rMsg);
            if (needle.Length < 4) continue;
            // 标题完全被 AI 文本包含，或重叠 token 比例高 → 视为重复
            if (hay.Contains(Normalize(rTitle), StringComparison.Ordinal) && rTitle.Length >= 4)
                return true;
            if (TokenOverlap(hay, needle) >= 0.55)
                return true;
        }
        return false;
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch >= 0x4e00)
                sb.Append(ch);
            else
                sb.Append(' ');
        }
        return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double TokenOverlap(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var inter = ta.Count(t => tb.Contains(t));
        return inter / (double)Math.Min(ta.Count, tb.Count);
    }

    private static HashSet<string> Tokenize(string s)
    {
        // 中文按双字滑窗，英文按词
        var set = new HashSet<string>(StringComparer.Ordinal);
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Length <= 6 && !p.Any(c => c >= 0x4e00))
            {
                set.Add(p);
                continue;
            }
            for (var i = 0; i + 2 <= p.Length; i++)
                set.Add(p.Substring(i, 2));
            if (p.Length >= 3)
                set.Add(p);
        }
        return set;
    }
}
