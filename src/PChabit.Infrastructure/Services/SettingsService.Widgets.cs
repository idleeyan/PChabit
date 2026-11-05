using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public partial class SettingsService
{
    // ===== 桌面硬件悬浮插件（3.23.0）=====

    public bool DesktopWidgetEnabled
    {
        get => _settings.DesktopWidgetEnabled;
        set
        {
            if (_settings.DesktopWidgetEnabled != value)
            {
                _settings.DesktopWidgetEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetEnabled) });
            }
        }
    }

    public bool DesktopWidgetShowCpu
    {
        get => _settings.DesktopWidgetShowCpu;
        set
        {
            if (_settings.DesktopWidgetShowCpu != value)
            {
                _settings.DesktopWidgetShowCpu = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowCpu) });
            }
        }
    }

    public bool DesktopWidgetShowMemory
    {
        get => _settings.DesktopWidgetShowMemory;
        set
        {
            if (_settings.DesktopWidgetShowMemory != value)
            {
                _settings.DesktopWidgetShowMemory = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowMemory) });
            }
        }
    }

    public bool DesktopWidgetShowGpu
    {
        get => _settings.DesktopWidgetShowGpu;
        set
        {
            if (_settings.DesktopWidgetShowGpu != value)
            {
                _settings.DesktopWidgetShowGpu = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowGpu) });
            }
        }
    }

    public bool DesktopWidgetShowVram
    {
        get => _settings.DesktopWidgetShowVram;
        set
        {
            if (_settings.DesktopWidgetShowVram != value)
            {
                _settings.DesktopWidgetShowVram = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowVram) });
            }
        }
    }

    public bool DesktopWidgetShowNet
    {
        get => _settings.DesktopWidgetShowNet;
        set
        {
            if (_settings.DesktopWidgetShowNet != value)
            {
                _settings.DesktopWidgetShowNet = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowNet) });
            }
        }
    }

    public bool DesktopWidgetShowDisk
    {
        get => _settings.DesktopWidgetShowDisk;
        set
        {
            if (_settings.DesktopWidgetShowDisk != value)
            {
                _settings.DesktopWidgetShowDisk = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowDisk) });
            }
        }
    }

    public bool DesktopWidgetShowUsage
    {
        get => _settings.DesktopWidgetShowUsage;
        set
        {
            if (_settings.DesktopWidgetShowUsage != value)
            {
                _settings.DesktopWidgetShowUsage = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowUsage) });
            }
        }
    }

    public bool DesktopWidgetShowCurrentApp
    {
        get => _settings.DesktopWidgetShowCurrentApp;
        set
        {
            if (_settings.DesktopWidgetShowCurrentApp != value)
            {
                _settings.DesktopWidgetShowCurrentApp = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetShowCurrentApp) });
            }
        }
    }

    public bool DesktopWidgetTopmost
    {
        get => _settings.DesktopWidgetTopmost;
        set
        {
            if (_settings.DesktopWidgetTopmost != value)
            {
                _settings.DesktopWidgetTopmost = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetTopmost) });
            }
        }
    }

    public bool DesktopWidgetClickThrough
    {
        get => _settings.DesktopWidgetClickThrough;
        set
        {
            if (_settings.DesktopWidgetClickThrough != value)
            {
                _settings.DesktopWidgetClickThrough = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetClickThrough) });
            }
        }
    }

    public bool DesktopWidgetClampToScreen
    {
        get => _settings.DesktopWidgetClampToScreen;
        set
        {
            if (_settings.DesktopWidgetClampToScreen != value)
            {
                _settings.DesktopWidgetClampToScreen = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetClampToScreen) });
            }
        }
    }

    public string DesktopWidgetScreen
    {
        get => _settings.DesktopWidgetScreen;
        set
        {
            if (_settings.DesktopWidgetScreen != value)
            {
                _settings.DesktopWidgetScreen = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetScreen) });
            }
        }
    }

    public int DesktopWidgetLeft
    {
        get => _settings.DesktopWidgetLeft;
        set
        {
            if (_settings.DesktopWidgetLeft != value)
            {
                _settings.DesktopWidgetLeft = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetLeft) });
            }
        }
    }

    public int DesktopWidgetTop
    {
        get => _settings.DesktopWidgetTop;
        set
        {
            if (_settings.DesktopWidgetTop != value)
            {
                _settings.DesktopWidgetTop = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetTop) });
            }
        }
    }

    public int DesktopWidgetWidth
    {
        get => _settings.DesktopWidgetWidth;
        set
        {
            if (_settings.DesktopWidgetWidth != value)
            {
                _settings.DesktopWidgetWidth = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetWidth) });
            }
        }
    }

    public int DesktopWidgetHeight
    {
        get => _settings.DesktopWidgetHeight;
        set
        {
            if (_settings.DesktopWidgetHeight != value)
            {
                _settings.DesktopWidgetHeight = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DesktopWidgetHeight) });
            }
        }
    }

    // ===== 便签（3.24.0）=====
    public bool StickyNotesEnabled
    {
        get => _settings.StickyNotesEnabled;
        set
        {
            if (_settings.StickyNotesEnabled != value)
            {
                _settings.StickyNotesEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesEnabled) });
            }
        }
    }

    public string StickyNotesHotkeyNew
    {
        get => _settings.StickyNotesHotkeyNew;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (_settings.StickyNotesHotkeyNew != v)
            {
                _settings.StickyNotesHotkeyNew = v;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesHotkeyNew) });
            }
        }
    }

    public string StickyNotesHotkeyBoard
    {
        get => _settings.StickyNotesHotkeyBoard;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (_settings.StickyNotesHotkeyBoard != v)
            {
                _settings.StickyNotesHotkeyBoard = v;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesHotkeyBoard) });
            }
        }
    }

    public bool StickyNotesSyncEnabled
    {
        get => _settings.StickyNotesSyncEnabled;
        set
        {
            if (_settings.StickyNotesSyncEnabled != value)
            {
                _settings.StickyNotesSyncEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesSyncEnabled) });
            }
        }
    }

    public DateTime? StickyNotesLastSync
    {
        get => _settings.StickyNotesLastSync;
        set
        {
            if (_settings.StickyNotesLastSync != value)
                _settings.StickyNotesLastSync = value; // 同步状态高频写，不触发设置变更事件
        }
    }

    public int StickyNotesRetentionDays
    {
        get => _settings.StickyNotesRetentionDays;
        set
        {
            var clamped = Math.Clamp(value, 1, 365);
            if (_settings.StickyNotesRetentionDays != clamped)
            {
                _settings.StickyNotesRetentionDays = clamped;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesRetentionDays) });
            }
        }
    }

    public bool StickyNotesQuickDismissOnFocusLost
    {
        get => _settings.StickyNotesQuickDismissOnFocusLost;
        set
        {
            if (_settings.StickyNotesQuickDismissOnFocusLost != value)
            {
                _settings.StickyNotesQuickDismissOnFocusLost = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StickyNotesQuickDismissOnFocusLost) });
            }
        }
    }

    public string StickyNotesDeviceId
    {
        get => _settings.StickyNotesDeviceId;
        set => _settings.StickyNotesDeviceId = value ?? "";
    }
}
