using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace PChabit.Infrastructure.Platform;

/// <summary>
/// 全局热键服务（3.24.0）：专用消息线程 + message-only 窗口 + RegisterHotKey。
/// WM_HOTKEY 直达本进程（即使最小化到托盘/前台是其他应用），MOD_NOREPEAT 防按住连发。
/// 线程阻塞在 GetMessage，空闲时零 CPU；注册失败（热键被占用）返回 false，由调用方降级提示。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public event Action<string>? HotkeyPressed;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_DESTROY = 0x0002;
    private const int WM_APP = 0x8000;
    private const int WM_APP_REGISTER = WM_APP + 1;
    private const int WM_APP_UNREGISTER = WM_APP + 2;
    private const int WM_APP_REBUILD = WM_APP + 3;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hWnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private sealed class HotkeyEntry
    {
        public required string ActionKey;
        public required string Gesture;     // 规范化后的 "Ctrl+Alt+N"
        public uint Modifiers;
        public uint Vk;
        public int Id;
        public bool Registered;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, HotkeyEntry> _entries = new(StringComparer.Ordinal);
    // 待处理项：RemovedId 仅在注销时携带（动作已从字典移除，必须靠它精确注销，不能扫描区间误伤其他热键）
    private readonly Queue<(string Key, int? RemovedId)> _pending = new();
    private IntPtr _hwnd;
    private uint _threadId;
    private Thread? _thread;
    private ManualResetEventSlim? _ready;
    private bool _disposed;
    private int _idSeq = 0xB001;

    /// <summary>启动消息线程（幂等）。</summary>
    public void Start()
    {
        if (_thread != null) return;
        _ready = new ManualResetEventSlim(false);
        _thread = new Thread(ThreadProc)
        {
            Name = "PChabit.GlobalHotkey",
            IsBackground = true
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// 注册（或替换）热键。gesture 形如 "Ctrl+Alt+N"；成功返回 true。
    /// 被其他软件占用/格式非法返回 false。空字符串表示不注册（注销该动作）。
    /// </summary>
    public bool Register(string actionKey, string gesture)
    {
        if (_disposed || string.IsNullOrWhiteSpace(actionKey)) return false;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            Unregister(actionKey);
            return true;
        }
        if (!TryParse(gesture, out uint mods, out uint vk, out string normalized))
            return false;

        lock (_gate)
        {
            _entries[actionKey] = new HotkeyEntry
            {
                ActionKey = actionKey,
                Gesture = normalized,
                Modifiers = mods,
                Vk = vk,
                Id = _entries.TryGetValue(actionKey, out var old) ? old.Id : _idSeq++
            };
            _pending.Enqueue((actionKey, null));
        }
        PostMessageW(_hwnd, WM_APP_REGISTER, IntPtr.Zero, IntPtr.Zero);
        // 同步取结果：消息处理完后队列中该动作的 Registered 标志即最终状态
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(actionKey, out var e) && !_pending.Any(p => p.Key == actionKey))
                    return e.Registered;
            }
            Thread.Sleep(15);
        }
        return false;
    }

    /// <summary>注销动作热键。</summary>
    public void Unregister(string actionKey)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(actionKey, out var old))
            {
                _entries.Remove(actionKey);
                _pending.Enqueue((actionKey, old.Id));
            }
            else return;
        }
        PostMessageW(_hwnd, WM_APP_UNREGISTER, IntPtr.Zero, IntPtr.Zero);
    }

    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        // 静态窗口类：RegisterClass 略繁，"Static" 系统类可直接 CreateWindowEx 承载消息
        _hwnd = CreateWindowExW(0, "Static", "PChabitHotkeyMsg", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _ready?.Set();

        // 启动积压的注册
        ProcessPending();

        while (true)
        {
            int ret = GetMessageW(out var msg, IntPtr.Zero, 0, 0);
            if (ret <= 0) break; // WM_QUIT(-1/0) 退出
            if (msg.message == WM_APP_REGISTER || msg.message == WM_APP_UNREGISTER || msg.message == WM_APP_REBUILD)
            {
                ProcessPending();
            }
            else if (msg.message == WM_HOTKEY)
            {
                string? action = null;
                lock (_gate)
                {
                    foreach (var e in _entries.Values)
                    {
                        if (e.Registered && e.Id == (int)msg.wParam) { action = e.ActionKey; break; }
                    }
                }
                if (action != null)
                {
                    try { HotkeyPressed?.Invoke(action); }
                    catch (Exception ex) { Log.Warning(ex, "[Hotkey] 动作 {Action} 处理异常", action); }
                }
            }
            else if (msg.hWnd == _hwnd && msg.message == WM_DESTROY)
            {
                break;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        lock (_gate)
        {
            foreach (var e in _entries.Values)
            {
                if (e.Registered) UnregisterHotKey(_hwnd, e.Id);
                e.Registered = false;
            }
        }
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }

    /// <summary>在热键线程上处理注册/注销队列（RegisterHotKey 必须在目标线程调用）。</summary>
    private void ProcessPending()
    {
        List<(string Key, int? RemovedId)> items;
        lock (_gate)
        {
            items = _pending.Distinct().ToList();
            _pending.Clear();
        }

        foreach (var (key, removedId) in items)
        {
            HotkeyEntry? entry;
            bool shouldExist;
            lock (_gate)
            {
                shouldExist = _entries.TryGetValue(key, out entry);
            }
            if (!shouldExist || entry == null)
            {
                // 已移除：精确注销携带的旧 id（不再扫描整个区间，避免误伤其他热键）
                if (removedId.HasValue)
                    UnregisterHotKey(_hwnd, removedId.Value);
                continue;
            }

            // 替换前先注销旧 id（幂等，失败无妨）
            UnregisterHotKey(_hwnd, entry.Id);
            bool ok = RegisterHotKey(_hwnd, entry.Id, entry.Modifiers | MOD_NOREPEAT, entry.Vk);
            entry.Registered = ok;
            if (ok)
                Log.Information("[Hotkey] 已注册 {Action} = {Gesture} (id={Id})", key, entry.Gesture, entry.Id);
            else
                Log.Warning("[Hotkey] 注册失败（可能被占用）: {Gesture} err={Err}", entry.Gesture, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>解析 "Ctrl+Alt+N" → 修饰位 + VK。至少一个 Ctrl/Alt/Win，主键仅一个。</summary>
    public static bool TryParse(string gesture, out uint modifiers, out uint vk, out string normalized)
    {
        modifiers = 0; vk = 0; normalized = "";
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        var modNames = new List<string>();
        string? main = null;
        foreach (var raw in parts)
        {
            var p = raw;
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; modNames.Add("Ctrl"); break;
                case "alt": modifiers |= MOD_ALT; modNames.Add("Alt"); break;
                case "shift": modifiers |= MOD_SHIFT; modNames.Add("Shift"); break;
                case "win" or "windows" or "super": modifiers |= MOD_WIN; modNames.Add("Win"); break;
                default:
                    if (main != null) return false; // 多个主键
                    main = p;
                    break;
            }
        }

        if (main == null || (modifiers & (MOD_CONTROL | MOD_ALT | MOD_WIN)) == 0) return false;
        if (!TryKeyToVk(main, out vk, out string mainName)) return false;

        normalized = string.Join("+", modNames) + "+" + mainName;
        return true;
    }

    private static bool TryKeyToVk(string key, out uint vk, out string name)
    {
        vk = 0; name = key.ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
            if (c is >= '0' and <= '9') { vk = c; return true; }
        }
        if (key.Length >= 2 && key[0] is 'F' or 'f' && int.TryParse(key.AsSpan(1), out int fn) && fn is >= 1 and <= 12)
        {
            vk = (uint)(0x70 + fn - 1); name = $"F{fn}"; return true;
        }
        // 常用扩展键
        var extra = new Dictionary<string, (uint Vk, string Name)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = (0x20, "Space"), ["Insert"] = (0x2D, "Insert"), ["Delete"] = (0x2E, "Delete"),
            ["Home"] = (0x24, "Home"), ["End"] = (0x23, "End"),
            ["PageUp"] = (0x21, "PageUp"), ["PageDown"] = (0x22, "PageDown"),
            ["Left"] = (0x25, "Left"), ["Up"] = (0x26, "Up"), ["Right"] = (0x27, "Right"), ["Down"] = (0x28, "Down"),
            ["Enter"] = (0x0D, "Enter"), ["Esc"] = (0x1B, "Esc"), ["Escape"] = (0x1B, "Esc"),
            ["Tab"] = (0x09, "Tab"), ["Comma"] = (0xBC, "Comma"), ["Period"] = (0xBE, "Period"),
        };
        if (extra.TryGetValue(key, out var v)) { vk = v.Vk; name = v.Name; return true; }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch { }
    }
}
