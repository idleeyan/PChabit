using Microsoft.Extensions.DependencyInjection;
using PChabit.App.Services;
using PChabit.App.Views;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Platform;
using Serilog;

namespace PChabit.App;

/// <summary>
/// App 的便签功能驱动分部（3.24.0）：
/// 全局热键（新建/便签列表）、快速录入窗单例、WebDAV 同步调度（周期 15 分钟 + 保存后 30 秒 debounce）、
/// 回收站墓碑过期清理。所有窗口操作经 DispatcherQueue 回到 UI 线程（热键回调来自专用消息线程）。
/// </summary>
public partial class App
{
    public const string HotkeyActionNew = "note.new";
    public const string HotkeyActionBoard = "note.board";

    private static readonly TimeSpan SyncPeriod = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SyncFirstDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SyncDebounceAfterSave = TimeSpan.FromSeconds(30);

    private readonly object _noteSyncGate = new();
    private GlobalHotkeyService? _noteHotkeys;
    private QuickNoteWindow? _quickNoteWindow;
    private Timer? _noteSyncTimer;
    private Timer? _noteSyncDebounce;
    private string? _appliedHotkeySig;
    private bool _stickyNotesInitialized;
    private int _noteSyncRunning;

    /// <summary>启动便签子系统（UI 线程，首次激活后与托盘一起初始化）。幂等。</summary>
    private void InitializeStickyNotes()
    {
        if (_stickyNotesInitialized) return;
        _stickyNotesInitialized = true;
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();

            if (_trayService != null)
            {
                _trayService.NewNoteRequested += (_, _) =>
                    _window?.DispatcherQueue.TryEnqueue(ShowQuickNoteWindow);
                _trayService.ShowNotesRequested += (_, _) =>
                    _window?.DispatcherQueue.TryEnqueue(ShowNotesBoard);
            }

            _noteHotkeys = _serviceProvider!.GetRequiredService<GlobalHotkeyService>();
            _noteHotkeys.HotkeyPressed += OnNoteHotkeyPressed;

            settings.SettingsChanged += OnNoteSettingsChanged;

            ApplyNoteHotkeys(settings);
            ConfigureNoteSyncTimer(settings);

            // 启动即清理过期墓碑（默认保留 30 天）
            //
            // 必须等数据库迁移真正完成：InitializeStickyNotes 在窗口首次激活后调用，而迁移在
            // App.OnLaunched 的 Task.Run 中跑，两者无先后保证。若不等，PurgeExpiredAsync 可能在
            // StickyNotes 表尚未创建时查询，抛 SQLite 异常 "no such table: StickyNotes"。
            // 解法与 StartBackupService 一致（见 App.xaml.cs:788）：WhenAny + 15 秒超时兜底，
            // 避免迁移被锁住时永久阻塞便签子系统。
            _ = Task.Run(async () =>
            {
                try
                {
                    var finished = await Task.WhenAny(_dbInitCompleted.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                    if (finished != _dbInitCompleted.Task)
                    {
                        Log.Warning("数据库初始化未在 15 秒内完成，跳过启动时便签清理（避免查询未建表的 StickyNotes）");
                        return;
                    }

                    var svc = _serviceProvider!.GetRequiredService<IStickyNoteService>();
                    int purged = await svc.PurgeExpiredAsync(settings.StickyNotesRetentionDays);
                    if (purged > 0) Log.Information("[Notes] 启动清理过期便签 {Count} 条", purged);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Notes] 启动清理过期便签失败");
                }
            });

            Log.Information("[Notes] 便签子系统已初始化（启用={Enabled}, 同步={Sync}）",
                settings.StickyNotesEnabled, settings.StickyNotesSyncEnabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Notes] 便签子系统初始化失败");
        }
    }

    private void OnNoteHotkeyPressed(string action)
    {
        try
        {
            if (action == HotkeyActionNew)
                _window?.DispatcherQueue.TryEnqueue(ShowQuickNoteWindow);
            else if (action == HotkeyActionBoard)
                _window?.DispatcherQueue.TryEnqueue(ShowNotesBoard);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 热键动作 {Action} 调度失败", action);
        }
    }

    private void OnNoteSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 只响应便签相关属性：其他模块（任务栏/WebDAV 回写等）在启动期也会写设置，
        // 全量重配会导致热键被无谓反复注册。PropertyName 为 null（未知批量变更）时才全量重配。
        if (e.PropertyName is string name && name is not (
            nameof(ISettingsService.StickyNotesEnabled)
            or nameof(ISettingsService.StickyNotesHotkeyNew)
            or nameof(ISettingsService.StickyNotesHotkeyBoard)
            or nameof(ISettingsService.StickyNotesSyncEnabled)
            or nameof(ISettingsService.WebDAVEnabled)))
            return;

        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            ApplyNoteHotkeys(settings);
            ConfigureNoteSyncTimer(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 响应设置变更重配失败");
        }
    }

    /// <summary>按当前设置（重新）注册两个全局热键；总开关关闭则全部注销。配置签名未变则跳过（Save() 会发 null 事件）。</summary>
    private void ApplyNoteHotkeys(ISettingsService settings)
    {
        var hk = _noteHotkeys;
        if (hk == null) return;
        try
        {
            string sig = settings.StickyNotesEnabled
                ? $"on|{settings.StickyNotesHotkeyNew}|{settings.StickyNotesHotkeyBoard}"
                : "off";
            if (sig == _appliedHotkeySig) return;

            if (!settings.StickyNotesEnabled)
            {
                hk.Unregister(HotkeyActionNew);
                hk.Unregister(HotkeyActionBoard);
                _appliedHotkeySig = sig;
                return;
            }
            hk.Start();
            bool okNew = hk.Register(HotkeyActionNew, settings.StickyNotesHotkeyNew);
            bool okBoard = hk.Register(HotkeyActionBoard, settings.StickyNotesHotkeyBoard);
            if (!okNew) Log.Warning("[Notes] 新建便签热键注册失败（被占用或格式错误）: {Gesture}", settings.StickyNotesHotkeyNew);
            if (!okBoard) Log.Warning("[Notes] 便签列表热键注册失败（被占用或格式错误）: {Gesture}", settings.StickyNotesHotkeyBoard);
            if (okNew && okBoard) _appliedHotkeySig = sig;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 热键注册异常");
        }
    }

    /// <summary>同步需要「便签同步」与「WebDAV」双开关；任一关闭即停止周期同步。</summary>
    private void ConfigureNoteSyncTimer(ISettingsService settings)
    {
        lock (_noteSyncGate)
        {
            bool shouldRun = settings.StickyNotesEnabled
                && settings.StickyNotesSyncEnabled
                && settings.WebDAVEnabled;

            if (shouldRun && _noteSyncTimer == null)
            {
                _noteSyncTimer = new Timer(
                    _ => _ = RunNoteSyncAsync("periodic"),
                    null, SyncFirstDelay, SyncPeriod);
                Log.Information("[Notes] WebDAV 同步调度已启动（首次 {First}s，周期 {Period}min）",
                    SyncFirstDelay.TotalSeconds, SyncPeriod.TotalMinutes);
            }
            else if (!shouldRun && _noteSyncTimer != null)
            {
                _noteSyncTimer.Dispose();
                _noteSyncTimer = null;
                Log.Information("[Notes] WebDAV 同步调度已停止");
            }
        }
    }

    private void ShowQuickNoteWindow()
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (!settings.StickyNotesEnabled) return;

            if (_quickNoteWindow == null)
            {
                _quickNoteWindow = new QuickNoteWindow(
                    _serviceProvider!.GetRequiredService<IStickyNoteService>(),
                    settings);
                _quickNoteWindow.NoteSaved += OnQuickNoteSaved;
            }
            _quickNoteWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 快速录入窗呼出失败");
        }
    }

    private void ShowNotesBoard()
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (!settings.StickyNotesEnabled) return;
            _trayService?.RestoreWindow();
            _serviceProvider?.GetService<NavigationService>()?.NavigateTo("Notes");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 打开便签列表失败");
        }
    }

    private void OnQuickNoteSaved()
    {
        // 保存后 30 秒 debounce 触发一次同步（连续记录只同步一次）
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (!settings.StickyNotesSyncEnabled || !settings.WebDAVEnabled) return;
            lock (_noteSyncGate)
            {
                _noteSyncDebounce ??= new Timer(
                    _ => _ = RunNoteSyncAsync("after-save"),
                    null, Timeout.Infinite, Timeout.Infinite);
                _noteSyncDebounce.Change(SyncDebounceAfterSave, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Notes] 保存后同步 debounce 调度失败");
        }
    }

    private async Task RunNoteSyncAsync(string reason)
    {
        if (Interlocked.Exchange(ref _noteSyncRunning, 1) == 1) return; // 上一轮未结束则跳过
        try
        {
            // 同步要读写 StickyNotes 表，必须等迁移完成。周期首次延迟 30s、debounce 30s，
            // 正常情况下迁移早已完成；这里仍加超时兜底，防止迁移被锁住时把表未建就当同步失败上报。
            var finished = await Task.WhenAny(_dbInitCompleted.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (finished != _dbInitCompleted.Task)
            {
                Log.Warning("[Notes] 数据库初始化未在 15 秒内完成，跳过本次同步({Reason})", reason);
                return;
            }

            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (!settings.StickyNotesEnabled || !settings.StickyNotesSyncEnabled || !settings.WebDAVEnabled)
                return;

            var sync = _serviceProvider!.GetRequiredService<Infrastructure.Services.StickyNoteSyncService>();
            var result = await sync.SyncAsync(CancellationToken.None);
            if (result.Ok)
            {
                settings.StickyNotesLastSync = DateTime.UtcNow;
                settings.Save();
                Log.Information("[Notes] 同步成功({Reason})：本地 {Local} / 云端 {Cloud} / 冲突 {Conflict}",
                    reason, result.LocalCount, result.CloudCount, result.ConflictCount);
            }
            else
            {
                Log.Warning("[Notes] 同步失败({Reason})：{Message}", reason, result.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 同步异常({Reason})", reason);
        }
        finally
        {
            Interlocked.Exchange(ref _noteSyncRunning, 0);
        }
    }

    /// <summary>退出流程调用：停同步定时器并注销全局热键（快速窗随进程退出）。</summary>
    private void StopStickyNotes()
    {
        lock (_noteSyncGate)
        {
            _noteSyncTimer?.Dispose();
            _noteSyncTimer = null;
            _noteSyncDebounce?.Dispose();
            _noteSyncDebounce = null;
        }
        _noteHotkeys?.Dispose();
        _noteHotkeys = null;
    }
}
