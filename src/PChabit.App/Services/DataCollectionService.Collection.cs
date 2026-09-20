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

    // 键盘/鼠标在内存中累计，由周期定时器统一落库，避免钩子事件驱动高频 SQLite 写入
    private readonly object _inputStatsLock = new();
    private MouseSession? _liveMouseSession;
    private KeyboardSession? _liveKeyboardSession;

    // 已提交水位：Flush 只写「相对上次落库的增量」，避免同一批次两个快照把同一段按键重复 +=
    private int _kbCommittedTotal;
    private int _kbCommittedBackspace;
    private int _kbCommittedDelete;
    private int _kbCommittedUndo;
    private readonly Dictionary<int, int> _kbCommittedFreq = new();
    private readonly Dictionary<string, int> _kbCommittedCatFreq = new();

    private int _msCommittedLeft;
    private int _msCommittedRight;
    private int _msCommittedMiddle;
    private int _msCommittedScroll;
    private double _msCommittedMove;

    private void ResetInputCommitWatermarks()
    {
        _kbCommittedTotal = 0;
        _kbCommittedBackspace = 0;
        _kbCommittedDelete = 0;
        _kbCommittedUndo = 0;
        _kbCommittedFreq.Clear();
        _kbCommittedCatFreq.Clear();
        _msCommittedLeft = 0;
        _msCommittedRight = 0;
        _msCommittedMiddle = 0;
        _msCommittedScroll = 0;
        _msCommittedMove = 0;
    }

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
        lock (_inputStatsLock)
        {
            EnsureLiveKeyboardSession(e.Timestamp, e.ActiveProcess ?? _currentProcessName);

            var session = _liveKeyboardSession!;
            var processName = e.ActiveProcess ?? _currentProcessName;
            if (!string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(session.ProcessName))
            {
                session.ProcessName = processName;
            }

            session.TotalKeyPresses += e.KeyCount;

            if (e.KeyCode > 0)
            {
                session.KeyFrequency[e.KeyCode] = session.KeyFrequency.GetValueOrDefault(e.KeyCode, 0) + e.KeyCount;
            }

            var category = GetKeyCategory(e.KeyCode, e.KeyName);
            if (!string.IsNullOrEmpty(category))
            {
                session.KeyCategoryFrequency[category] = session.KeyCategoryFrequency.GetValueOrDefault(category, 0) + e.KeyCount;
            }

            if (e.KeyCode == 0x08)
                session.BackspaceCount += e.KeyCount;
            else if (e.KeyCode == 0x2E)
                session.DeleteCount += e.KeyCount;

            if (e.IsCtrlPressed && e.KeyCode == 0x5A)
                session.UndoCount += e.KeyCount;

            if (e.IsShortcut && !string.IsNullOrEmpty(e.KeyName))
            {
                session.Shortcuts.Add(new ShortcutUsage
                {
                    Timestamp = e.Timestamp,
                    Shortcut = e.GetShortcutString(),
                    Application = processName
                });
            }

            UpdateTypingSpeed(session, e);
        }
    }

    private void EnsureLiveKeyboardSession(DateTime timestamp, string? processName)
    {
        var date = timestamp.Date;
        var hour = timestamp.Hour;

        if (_liveKeyboardSession != null &&
            (_liveKeyboardSession.Date != date || _liveKeyboardSession.Hour != hour))
        {
            var previous = CloneKeyboardSession(_liveKeyboardSession);
            EnqueueOperation(db => UpsertKeyboardSessionAsync(db, previous, isDelta: false), "键盘会话整点切换落库");
            _liveKeyboardSession = null;
            ResetInputCommitWatermarks();
        }

        _liveKeyboardSession ??= new KeyboardSession
        {
            Id = Guid.NewGuid(),
            Date = date,
            Hour = hour,
            ProcessName = processName,
            KeyFrequency = new Dictionary<int, int>(),
            KeyCategoryFrequency = new Dictionary<string, int>(),
            TypingBursts = new List<TypingBurst>(),
            Shortcuts = new List<ShortcutUsage>()
        };
    }

    private void EnsureLiveMouseSession(DateTime timestamp)
    {
        var date = timestamp.Date;
        var hour = timestamp.Hour;

        if (_liveMouseSession != null &&
            (_liveMouseSession.Date != date || _liveMouseSession.Hour != hour))
        {
            var previous = CloneMouseSession(_liveMouseSession);
            EnqueueOperation(db => UpsertMouseSessionAsync(db, previous, isDelta: false), "鼠标会话整点切换落库");
            _liveMouseSession = null;
            // 鼠标与键盘共用水位时，键盘切换会 reset；这里只清鼠标侧
            _msCommittedLeft = 0;
            _msCommittedRight = 0;
            _msCommittedMiddle = 0;
            _msCommittedScroll = 0;
            _msCommittedMove = 0;
        }

        _liveMouseSession ??= new MouseSession
        {
            Id = Guid.NewGuid(),
            Date = date,
            Hour = hour,
            ProcessName = _currentProcessName
        };

        if (string.IsNullOrEmpty(_liveMouseSession.ProcessName) && !string.IsNullOrEmpty(_currentProcessName))
        {
            _liveMouseSession.ProcessName = _currentProcessName;
        }
    }

    private void UpdateTypingSpeed(KeyboardSession session, KeyboardEventArgs e)
    {
        var now = e.Timestamp;
        var burstKey = (now.Date, session.Hour);

        if (!_typingBursts.TryGetValue(burstKey, out var burst))
        {
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

            session.TypingBursts.Add(new TypingBurst
            {
                StartTime = burst.startTime,
                Duration = duration,
                KeyCount = burst.keyCount
            });

            if (kpm > session.PeakTypingSpeed)
            {
                session.PeakTypingSpeed = kpm;
            }

            if (session.TypingBursts.Count > 0)
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
        lock (_inputStatsLock)
        {
            EnsureLiveMouseSession(e.Timestamp);
            var session = _liveMouseSession!;

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
        }
    }

    private void OnMouseMove(object? sender, MouseMoveEventArgs e)
    {
        if (e.Distance < 1) return;

        lock (_inputStatsLock)
        {
            EnsureLiveMouseSession(e.Timestamp);
            _liveMouseSession!.TotalMoveDistance += e.Distance;
        }
    }

    private void OnMouseScroll(object? sender, MouseScrollEventArgs e)
    {
        lock (_inputStatsLock)
        {
            EnsureLiveMouseSession(e.Timestamp);
            _liveMouseSession!.ScrollCount++;
        }
    }

    private void FlushLiveInputSessions()
    {
        lock (_inputStatsLock)
        {
            if (_liveKeyboardSession != null)
            {
                var live = _liveKeyboardSession;
                var dTotal = live.TotalKeyPresses - _kbCommittedTotal;
                var dBack = live.BackspaceCount - _kbCommittedBackspace;
                var dDel = live.DeleteCount - _kbCommittedDelete;
                var dUndo = live.UndoCount - _kbCommittedUndo;

                var dFreq = new Dictionary<int, int>();
                foreach (var kv in live.KeyFrequency)
                {
                    var delta = kv.Value - _kbCommittedFreq.GetValueOrDefault(kv.Key, 0);
                    if (delta > 0) dFreq[kv.Key] = delta;
                }

                var dCat = new Dictionary<string, int>();
                foreach (var kv in live.KeyCategoryFrequency)
                {
                    var delta = kv.Value - _kbCommittedCatFreq.GetValueOrDefault(kv.Key, 0);
                    if (delta > 0) dCat[kv.Key] = delta;
                }

                if (dTotal != 0 || dFreq.Count > 0 || dBack != 0 || dDel != 0 || dUndo != 0)
                {
                    var snapshot = new KeyboardSession
                    {
                        Id = live.Id,
                        Date = live.Date,
                        Hour = live.Hour,
                        ProcessName = live.ProcessName,
                        // 存增量；Upsert 用 isDelta=true 做 +=
                        TotalKeyPresses = dTotal,
                        KeyFrequency = dFreq,
                        KeyCategoryFrequency = dCat,
                        BackspaceCount = dBack,
                        DeleteCount = dDel,
                        UndoCount = dUndo,
                        AverageTypingSpeed = live.AverageTypingSpeed,
                        PeakTypingSpeed = live.PeakTypingSpeed,
                        TypingBursts = new List<TypingBurst>(),
                        Shortcuts = new List<ShortcutUsage>()
                    };
                    EnqueueOperation(db => UpsertKeyboardSessionAsync(db, snapshot, isDelta: true), "键盘会话周期落库(增量)");

                    _kbCommittedTotal = live.TotalKeyPresses;
                    _kbCommittedBackspace = live.BackspaceCount;
                    _kbCommittedDelete = live.DeleteCount;
                    _kbCommittedUndo = live.UndoCount;
                    foreach (var kv in live.KeyFrequency) _kbCommittedFreq[kv.Key] = kv.Value;
                    foreach (var kv in live.KeyCategoryFrequency) _kbCommittedCatFreq[kv.Key] = kv.Value;
                }
            }

            if (_liveMouseSession != null)
            {
                var live = _liveMouseSession;
                var dL = live.LeftClickCount - _msCommittedLeft;
                var dR = live.RightClickCount - _msCommittedRight;
                var dM = live.MiddleClickCount - _msCommittedMiddle;
                var dS = live.ScrollCount - _msCommittedScroll;
                var dMove = live.TotalMoveDistance - _msCommittedMove;

                if (dL != 0 || dR != 0 || dM != 0 || dS != 0 || dMove > 0.5)
                {
                    var snapshot = new MouseSession
                    {
                        Id = live.Id,
                        Date = live.Date,
                        Hour = live.Hour,
                        ProcessName = live.ProcessName,
                        LeftClickCount = dL,
                        RightClickCount = dR,
                        MiddleClickCount = dM,
                        ScrollCount = dS,
                        TotalMoveDistance = dMove
                    };
                    EnqueueOperation(db => UpsertMouseSessionAsync(db, snapshot, isDelta: true), "鼠标会话周期落库(增量)");

                    _msCommittedLeft = live.LeftClickCount;
                    _msCommittedRight = live.RightClickCount;
                    _msCommittedMiddle = live.MiddleClickCount;
                    _msCommittedScroll = live.ScrollCount;
                    _msCommittedMove = live.TotalMoveDistance;
                }
            }
        }
    }

    private static KeyboardSession CloneKeyboardSession(KeyboardSession source) => new()
    {
        Id = source.Id,
        Date = source.Date,
        Hour = source.Hour,
        ProcessName = source.ProcessName,
        TotalKeyPresses = source.TotalKeyPresses,
        KeyFrequency = new Dictionary<int, int>(source.KeyFrequency),
        KeyCategoryFrequency = new Dictionary<string, int>(source.KeyCategoryFrequency),
        AverageTypingSpeed = source.AverageTypingSpeed,
        PeakTypingSpeed = source.PeakTypingSpeed,
        UndoCount = source.UndoCount,
        DeleteCount = source.DeleteCount,
        BackspaceCount = source.BackspaceCount,
        TypingBursts = new List<TypingBurst>(source.TypingBursts),
        Shortcuts = new List<ShortcutUsage>(source.Shortcuts)
    };

    private static MouseSession CloneMouseSession(MouseSession source) => new()
    {
        Id = source.Id,
        Date = source.Date,
        Hour = source.Hour,
        ProcessName = source.ProcessName,
        LeftClickCount = source.LeftClickCount,
        RightClickCount = source.RightClickCount,
        MiddleClickCount = source.MiddleClickCount,
        ScrollCount = source.ScrollCount,
        TotalMoveDistance = source.TotalMoveDistance
    };

    private static async Task UpsertKeyboardSessionAsync(PChabitDbContext dbContext, KeyboardSession live, bool isDelta)
    {
        var existing = await dbContext.KeyboardSessions
            .FirstOrDefaultAsync(s => s.Date == live.Date && s.Hour == live.Hour);

        if (existing == null)
        {
            if (isDelta)
            {
                // 增量写入新行：直接用增量值
                dbContext.KeyboardSessions.Add(live);
            }
            else
            {
                dbContext.KeyboardSessions.Add(live);
            }
            await dbContext.SaveChangesAsync();
            return;
        }

        if (isDelta)
        {
            existing.TotalKeyPresses += Math.Max(0, live.TotalKeyPresses);
            existing.BackspaceCount += Math.Max(0, live.BackspaceCount);
            existing.DeleteCount += Math.Max(0, live.DeleteCount);
            existing.UndoCount += Math.Max(0, live.UndoCount);
            foreach (var kv in live.KeyFrequency)
            {
                existing.KeyFrequency[kv.Key] = existing.KeyFrequency.GetValueOrDefault(kv.Key, 0) + kv.Value;
            }
            foreach (var kv in live.KeyCategoryFrequency)
            {
                existing.KeyCategoryFrequency[kv.Key] = existing.KeyCategoryFrequency.GetValueOrDefault(kv.Key, 0) + kv.Value;
            }
            if (live.PeakTypingSpeed > existing.PeakTypingSpeed)
                existing.PeakTypingSpeed = live.PeakTypingSpeed;
            if (live.AverageTypingSpeed > 0)
                existing.AverageTypingSpeed = live.AverageTypingSpeed;
            if (!string.IsNullOrEmpty(live.ProcessName))
                existing.ProcessName = live.ProcessName;
            await dbContext.SaveChangesAsync();
            return;
        }

        // 全量替换（整点切换时的最终快照）
        if (existing.Id == live.Id || existing.Date == live.Date && existing.Hour == live.Hour)
        {
            existing.TotalKeyPresses = live.TotalKeyPresses;
            existing.KeyFrequency = new Dictionary<int, int>(live.KeyFrequency);
            existing.KeyCategoryFrequency = new Dictionary<string, int>(live.KeyCategoryFrequency);
            existing.AverageTypingSpeed = live.AverageTypingSpeed;
            existing.PeakTypingSpeed = live.PeakTypingSpeed;
            existing.UndoCount = live.UndoCount;
            existing.DeleteCount = live.DeleteCount;
            existing.BackspaceCount = live.BackspaceCount;
            existing.TypingBursts = new List<TypingBurst>(live.TypingBursts);
            existing.Shortcuts = new List<ShortcutUsage>(live.Shortcuts);
            if (!string.IsNullOrEmpty(live.ProcessName))
                existing.ProcessName = live.ProcessName;
            await dbContext.SaveChangesAsync();
            return;
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task UpsertMouseSessionAsync(PChabitDbContext dbContext, MouseSession live, bool isDelta)
    {
        var existing = await dbContext.MouseSessions
            .FirstOrDefaultAsync(s => s.Date == live.Date && s.Hour == live.Hour);

        if (existing == null)
        {
            dbContext.MouseSessions.Add(live);
            await dbContext.SaveChangesAsync();
            return;
        }

        if (isDelta)
        {
            existing.LeftClickCount += Math.Max(0, live.LeftClickCount);
            existing.RightClickCount += Math.Max(0, live.RightClickCount);
            existing.MiddleClickCount += Math.Max(0, live.MiddleClickCount);
            existing.ScrollCount += Math.Max(0, live.ScrollCount);
            existing.TotalMoveDistance += Math.Max(0, live.TotalMoveDistance);
            if (!string.IsNullOrEmpty(live.ProcessName))
                existing.ProcessName = live.ProcessName;
            await dbContext.SaveChangesAsync();
            return;
        }

        existing.LeftClickCount = live.LeftClickCount;
        existing.RightClickCount = live.RightClickCount;
        existing.MiddleClickCount = live.MiddleClickCount;
        existing.ScrollCount = live.ScrollCount;
        existing.TotalMoveDistance = live.TotalMoveDistance;
        if (!string.IsNullOrEmpty(live.ProcessName))
            existing.ProcessName = live.ProcessName;
        await dbContext.SaveChangesAsync();
    }
}
