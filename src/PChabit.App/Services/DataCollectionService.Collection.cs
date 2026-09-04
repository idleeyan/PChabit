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
    private readonly Dictionary<(DateTime Date, int Hour), (DateTime startTime, int keyCount)> _typingBursts = new();
    private const int TypingBurstThresholdMs = 1000;
    private void OnAppDataCollected(object? sender, AppActiveEventArgs e)
    {
        lock (_lock)
        {
            var isBackgroundApp = _backgroundAppSettings.IsBackgroundApp(e.ProcessName);

            if (isBackgroundApp)
            {
                if (!_backgroundSessions.ContainsKey(e.ProcessName))
                {
                    _backgroundSessions[e.ProcessName] = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = e.ProcessName,
                        WindowTitle = e.WindowTitle,
                        ExecutablePath = e.ExecutablePath,
                        StartTime = e.Timestamp,
                        AppName = e.AppName,
                        Category = e.Category
                    };
                }
                else
                {
                    var existingSession = _backgroundSessions[e.ProcessName];
                    if (existingSession.WindowTitle != e.WindowTitle)
                    {
                        EnqueueSaveBackgroundSession(e.ProcessName);
                        _backgroundSessions[e.ProcessName] = new AppSession
                        {
                            Id = Guid.NewGuid(),
                            ProcessName = e.ProcessName,
                            WindowTitle = e.WindowTitle,
                            ExecutablePath = e.ExecutablePath,
                            StartTime = e.Timestamp,
                            AppName = e.AppName,
                            Category = e.Category
                        };
                    }
                }
            }

            if (_currentProcessName != e.ProcessName &&
                _backgroundAppSettings.IsBackgroundApp(_currentProcessName))
            {
                EnqueueSaveBackgroundSession(_currentProcessName);
            }
            else
            {
                EnqueueSaveCurrentSession();
            }

            _currentProcessName = e.ProcessName;

            if (!isBackgroundApp)
            {
                _currentAppSession = new AppSession
                {
                    Id = Guid.NewGuid(),
                    ProcessName = e.ProcessName,
                    WindowTitle = e.WindowTitle,
                    ExecutablePath = e.ExecutablePath,
                    StartTime = e.Timestamp,
                    AppName = e.AppName,
                    Category = e.Category
                };
            }
            else
            {
                _currentAppSession = null;
            }
        }
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
    {
        lock (_lock)
        {
            if (_backgroundAppSettings.IsBackgroundApp(e.ProcessName))
            {
                if (_backgroundSessions.TryGetValue(e.ProcessName, out var session))
                {
                    EnqueueSaveBackgroundSession(e.ProcessName);
                    _backgroundSessions[e.ProcessName] = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = e.ProcessName,
                        WindowTitle = e.WindowTitle,
                        StartTime = e.Timestamp,
                        AppName = session.AppName,
                        Category = session.Category
                    };
                }
            }
            else if (_currentAppSession != null && _currentAppSession.ProcessName == e.ProcessName)
            {
                EnqueueSaveCurrentSession();
                _currentAppSession = new AppSession
                {
                    Id = Guid.NewGuid(),
                    ProcessName = e.ProcessName,
                    WindowTitle = e.WindowTitle,
                    StartTime = e.Timestamp,
                    AppName = _currentAppSession.AppName,
                    Category = _currentAppSession.Category
                };
            }
        }
    }

    private void OnKeyboardDataCollected(object? sender, KeyboardEventArgs e)
    {
        EnqueueOperation(async dbContext =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;
            var processName = e.ActiveProcess ?? _currentProcessName;

            var session = await dbContext.KeyboardSessions.FirstOrDefaultAsync(s => s.Date >= date && s.Date < date.AddDays(1) && s.Hour == hour);
            if (session == null)
            {
                session = new KeyboardSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    TotalKeyPresses = 0,
                    ProcessName = processName,
                    KeyFrequency = new Dictionary<int, int>(),
                    KeyCategoryFrequency = new Dictionary<string, int>()
                };
                dbContext.KeyboardSessions.Add(session);
            }

            session.TotalKeyPresses += e.KeyCount;

            if (!string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(session.ProcessName))
            {
                session.ProcessName = processName;
            }

            if (e.KeyCode > 0)
            {
                var freq = new Dictionary<int, int>(session.KeyFrequency);
                freq[e.KeyCode] = freq.GetValueOrDefault(e.KeyCode, 0) + e.KeyCount;
                session.KeyFrequency = freq;
            }

            var category = GetKeyCategory(e.KeyCode, e.KeyName);
            if (!string.IsNullOrEmpty(category))
            {
                var catFreq = new Dictionary<string, int>(session.KeyCategoryFrequency);
                catFreq[category] = catFreq.GetValueOrDefault(category, 0) + e.KeyCount;
                session.KeyCategoryFrequency = catFreq;
            }

            if (e.KeyCode == 0x08)
                session.BackspaceCount += e.KeyCount;
            else if (e.KeyCode == 0x2E)
                session.DeleteCount += e.KeyCount;

            if (e.IsCtrlPressed && e.KeyCode == 0x5A) // Ctrl+Z = Undo
                session.UndoCount += e.KeyCount;

            if (e.IsShortcut && !string.IsNullOrEmpty(e.KeyName))
            {
                var shortcut = e.GetShortcutString();
                var shortcuts = new List<ShortcutUsage>(session.Shortcuts?.Count > 0 ? session.Shortcuts : Enumerable.Empty<ShortcutUsage>())
                {
                    new ShortcutUsage
                    {
                        Timestamp = e.Timestamp,
                        Shortcut = shortcut,
                        Application = processName
                    }
                };
                session.Shortcuts = shortcuts;
            }

            UpdateTypingSpeed(session, e);
        }, $"键盘数据 {e.Timestamp:HH:mm}");
    }

    private void UpdateTypingSpeed(KeyboardSession session, KeyboardEventArgs e)
    {
        var now = e.Timestamp;
        var burstKey = (now.Date, session.Hour);

        if (!_typingBursts.TryGetValue(burstKey, out var burst))
        {
            // 清理超过 2 小时前的旧条目，防止内存泄漏
            var cutoff = now.AddHours(-2);
            var staleKeys = _typingBursts.Keys.Where(k => k.Date < cutoff.Date || (k.Date == cutoff.Date && k.Hour < cutoff.Hour)).ToList();
            foreach (var key in staleKeys)
                _typingBursts.Remove(key);

            burst = (now, 0);
        }

        var timeSinceLastKey = (now - burst.startTime).TotalMilliseconds;

        if (timeSinceLastKey > TypingBurstThresholdMs && burst.keyCount > 0)
        {
            var duration = now - burst.startTime;
            var kpm = burst.keyCount > 0 && duration.TotalMinutes > 0
                ? burst.keyCount / duration.TotalMinutes
                : 0;

            var existingBursts = session.TypingBursts ?? Enumerable.Empty<TypingBurst>();
            session.TypingBursts = new List<TypingBurst>(existingBursts)
            {
                new TypingBurst
                {
                    StartTime = burst.startTime,
                    Duration = duration,
                    KeyCount = burst.keyCount
                }
            };

            if (kpm > session.PeakTypingSpeed)
            {
                session.PeakTypingSpeed = kpm;
            }

            var totalBursts = session.TypingBursts.Count;
            if (totalBursts > 0)
            {
                session.AverageTypingSpeed = session.TypingBursts.Average(b => b.KeyCount / Math.Max(b.Duration.TotalMinutes, 0.001));
            }

            burst = (now, 0);
        }

        burst.keyCount += e.KeyCount;
        _typingBursts[burstKey] = burst;
    }

    private static string GetKeyCategory(int keyCode, string? keyName)
    {
        if (keyCode >= 0x41 && keyCode <= 0x5A) return "字母";
        if (keyCode >= 0x30 && keyCode <= 0x39) return "数字";
        if (keyCode >= 0x70 && keyCode <= 0x87) return "功能键";
        if (keyCode == 0x20) return "空格";
        if (keyCode == 0x0D) return "回车";
        if (keyCode == 0x08) return "退格";
        if (keyCode == 0x2E) return "删除";
        if (keyCode == 0x09) return "Tab";
        if (keyCode == 0x1B) return "Esc";
        if (keyCode >= 0x25 && keyCode <= 0x28) return "方向键";
        return "其他";
    }

    private void OnMouseClick(object? sender, MouseClickEventArgs e)
    {
        EnqueueOperation(async dbContext =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await dbContext.MouseSessions.FirstOrDefaultAsync(s => s.Date >= date && s.Date < date.AddDays(1) && s.Hour == hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                dbContext.MouseSessions.Add(session);
            }

            switch (e.Button)
            {
                case MouseButtonType.Left:
                    session.LeftClickCount++;
                    break;
                case MouseButtonType.Right:
                    session.RightClickCount++;
                    break;
                case MouseButtonType.Middle:
                    session.MiddleClickCount++;
                    break;
            }
        }, $"鼠标点击 {e.Button}");
    }

    private void OnMouseMove(object? sender, MouseMoveEventArgs e)
    {
        if (e.Distance < 1) return;

        EnqueueOperation(async dbContext =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await dbContext.MouseSessions.FirstOrDefaultAsync(s => s.Date >= date && s.Date < date.AddDays(1) && s.Hour == hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                dbContext.MouseSessions.Add(session);
            }

            session.TotalMoveDistance += e.Distance;
        }, "鼠标移动");
    }

    private void OnMouseScroll(object? sender, MouseScrollEventArgs e)
    {
        EnqueueOperation(async dbContext =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await dbContext.MouseSessions.FirstOrDefaultAsync(s => s.Date >= date && s.Date < date.AddDays(1) && s.Hour == hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                dbContext.MouseSessions.Add(session);
            }

            session.ScrollCount++;
        }, "鼠标滚动");
    }

}

