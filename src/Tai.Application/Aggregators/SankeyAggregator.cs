using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Application.Aggregators;

public class SankeyNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public TimeSpan TotalUsage { get; set; }
}

public class SankeyLink
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int SwitchCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
}

public class SankeyData
{
    public List<SankeyNode> Nodes { get; set; } = new();
    public List<SankeyLink> Links { get; set; } = new();
}

public class SankeyAggregator
{
    private readonly IUnitOfWork _unitOfWork;

    public SankeyAggregator(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SankeyData> GetSankeyDataAsync(DateTime startDate, DateTime endDate, int topN = 10)
    {
        var sessions = await _unitOfWork.AppSessions.GetByDateRangeAsync(startDate, endDate);
        var sortedSessions = sessions.OrderBy(s => s.StartTime).ToList();

        var appUsage = new Dictionary<string, TimeSpan>();
        var transitions = new Dictionary<string, int>();

        string? previousProcess = null;
        DateTime? previousEndTime = null;

        foreach (var session in sortedSessions)
        {
            var processName = GetAppDisplayName(session.ProcessName ?? "Unknown");

            if (!appUsage.ContainsKey(processName))
                appUsage[processName] = TimeSpan.Zero;
            appUsage[processName] += session.Duration;

            if (previousProcess != null && previousProcess != processName)
            {
                if (previousEndTime.HasValue)
                {
                    var timeGap = session.StartTime - previousEndTime.Value;
                    if (timeGap.TotalSeconds <= 30)
                    {
                        var key = $"{previousProcess}->{processName}";
                        if (!transitions.ContainsKey(key))
                            transitions[key] = 0;
                        transitions[key]++;
                    }
                }
            }

            previousProcess = processName;
            previousEndTime = session.EndTime;
        }

        var topApps = appUsage
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => kv.Key)
            .ToHashSet();

        var nodes = new List<SankeyNode>();
        var nodeIdMap = new Dictionary<string, string>();
        var otherUsage = TimeSpan.Zero;

        foreach (var kvp in appUsage.OrderByDescending(kv => kv.Value))
        {
            if (topApps.Contains(kvp.Key))
            {
                var nodeId = Guid.NewGuid().ToString();
                nodes.Add(new SankeyNode
                {
                    Id = nodeId,
                    Name = kvp.Key,
                    ProcessName = kvp.Key,
                    TotalUsage = kvp.Value
                });
                nodeIdMap[kvp.Key] = nodeId;
            }
            else
            {
                otherUsage += kvp.Value;
            }
        }

        var otherId = Guid.NewGuid().ToString();
        nodes.Add(new SankeyNode
        {
            Id = otherId,
            Name = "其他应用",
            ProcessName = "Other",
            TotalUsage = otherUsage
        });
        nodeIdMap["Other"] = otherId;

        var links = new List<SankeyLink>();
        var linkMap = new Dictionary<(string, string), SankeyLink>();

        foreach (var kvp in transitions)
        {
            var parts = kvp.Key.Split("->");
            var source = parts[0];
            var target = parts[1];

            var sourceId = nodeIdMap.TryGetValue(source, out var sId) ? sId : nodeIdMap["Other"];
            var targetId = nodeIdMap.TryGetValue(target, out var tId) ? tId : nodeIdMap["Other"];

            if (sourceId == targetId)
                continue;

            var key = (sourceId, targetId);
            var reverseKey = (targetId, sourceId);

            if (linkMap.ContainsKey(reverseKey))
            {
                linkMap[reverseKey].SwitchCount += kvp.Value;
            }
            else if (!linkMap.ContainsKey(key))
            {
                linkMap[key] = new SankeyLink
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    SwitchCount = kvp.Value,
                    TotalDuration = TimeSpan.Zero
                };
            }
            else
            {
                linkMap[key].SwitchCount += kvp.Value;
            }
        }

        links = linkMap.Values.OrderByDescending(l => l.SwitchCount).ToList();

        return new SankeyData
        {
            Nodes = nodes,
            Links = links
        };
    }

    private string GetAppDisplayName(string processName)
    {
        return processName
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".EXE", "", StringComparison.OrdinalIgnoreCase);
    }
}
