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

namespace PChabit.App.Services;

public partial class DataCollectionService : IDisposable
{
    private void OnWebActivityReceived(object? sender, WebActivityEventArgs e)
    {
        var sessionKey = $"{e.ClientId}_{e.TabId}";

        switch (e.ActivityType)
        {
            case WebActivityType.PageView:
            case WebActivityType.TabSwitch:
                HandlePageView(e, sessionKey);
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
        }
    }

    private void HandlePageView(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var existingSession))
            {
                existingSession.EndTime = DateTime.Now;
                existingSession.Duration = existingSession.EndTime.Value - existingSession.StartTime;
                EnqueueSaveWebSession(existingSession);
            }

            var timestamp = e.Timestamp;
            if (timestamp.Kind == DateTimeKind.Utc)
            {
                timestamp = timestamp.ToLocalTime();
            }

            var webSession = new WebSession
            {
                Id = Guid.NewGuid(),
                Url = e.Url,
                Title = e.Title,
                Domain = e.Domain,
                Browser = e.Browser,
                TabId = e.TabId,
                StartTime = timestamp,
                ScrollDepth = 0,
                ClickCount = 0,
                HasFormInteraction = false,
                IsActiveTab = true
            };

            _activeWebSessions[sessionKey] = webSession;
        }
    }

    private void HandlePageClose(string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                _activeWebSessions.Remove(sessionKey);
                session.EndTime = DateTime.Now;
                session.Duration = session.EndTime.Value - session.StartTime;
                session.IsActiveTab = false;
                EnqueueSaveWebSession(session);
            }
        }
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
                if (_activeWebSessions.TryGetValue(key, out var session))
                {
                    _activeWebSessions.Remove(key);
                    session.EndTime = DateTime.Now;
                    session.Duration = session.EndTime.Value - session.StartTime;
                    session.IsActiveTab = false;
                    EnqueueSaveWebSession(session);
                }
            }
        }
    }

}

