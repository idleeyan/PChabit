using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PChabit.Infrastructure.Monitoring;

/// <summary>
/// 专用输入钩子线程。WH_MOUSE_LL / WH_KEYBOARD_LL 以及 WINEVENT 回调依赖安装线程的消息泵。
/// 若装在 UI 线程，布局/渲染/GC 会拖慢钩子回调，表现为全系统鼠标卡顿。
/// </summary>
public sealed class InputHookThread : IDisposable
{
    private const uint WM_QUIT = 0x0012;
    private const uint PM_REMOVE = 0x0001;
    private const int WM_APP_DISPATCH = 0x8000 + 42;

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _running;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _started = new(false);

    public bool IsRunning => _running && _thread is { IsAlive: true };

    public void Start()
    {
        if (IsRunning) return;

        _running = true;
        _started.Reset();

        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "PChabit-InputHooks",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            _running = false;
            throw new TimeoutException("输入钩子线程启动超时");
        }
    }

    public void Post(Action action)
    {
        if (!_running || _threadId == 0) return;
        _queue.Enqueue(action);
        PostThreadMessage(_threadId, WM_APP_DISPATCH, IntPtr.Zero, IntPtr.Zero);
    }

    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();

        // 强制创建消息队列，否则 PostThreadMessage 可能失败
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        _started.Set();

        while (_running)
        {
            var result = GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (result <= 0) break;

            if (msg.message == WM_APP_DISPATCH)
            {
                while (_queue.TryDequeue(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                        // 钩子动作异常不应结束消息循环
                    }
                }
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        while (_queue.TryDequeue(out _))
        {
        }
    }

    public void Stop()
    {
        if (_thread is not { IsAlive: true })
        {
            _running = false;
            return;
        }

        _running = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            Serilog.Log.Warning("输入钩子线程未能在 2 秒内退出");
        }

        _threadId = 0;
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _started.Dispose();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
}
