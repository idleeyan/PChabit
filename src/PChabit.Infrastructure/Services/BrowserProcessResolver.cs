using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 通过本机 TCP 连接端口反查拥有该连接的进程，从而精准识别浏览器。
/// 识别证据按可靠性排序：
///   1) 可执行文件完整路径（Chromium 系浏览器各安装目录含品牌目录，最可靠）
///   2) 进程名（兜底）
/// 不依赖 UA / 内核版本（豆包、Chrome、Edge 的 Chromium UA 几乎相同，且版本会更新）。
///
/// 注意：Chromium 的 WebSocket 连接由 NetworkService（utility 子进程）建立，
/// 且现代浏览器对 localhost 优先走 IPv6 回环（[::1]），因此必须同时查 IPv4 与 IPv6 TCP 表。
/// </summary>
public static class BrowserProcessResolver
{
    // ---------- 原生结构体 / 常量 ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPTABLE_OWNER_PID
    {
        public uint dwNumEntries;
        // 后接 MIB_TCPROW_OWNER_PID[]
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, uint Reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    // ---------- TCP 表反查 ----------

    /// <summary>本地端口 → 进程 PID；找不到返回 0。IPv4 与 IPv6 都查。</summary>
    public static int GetPidByLocalPort(int localPort)
    {
        if (localPort <= 0) return 0;
        var pid = ScanTcpTable(AF_INET, (lp, rp) => lp == localPort);
        if (pid != 0) return pid;
        return ScanTcpTable(AF_INET6, (lp, rp) => lp == localPort);
    }

    /// <summary>
    /// 由「连到本服务端口的客户端源端口」反查浏览器进程 PID。
    /// 本机回环：浏览器 ephemeral 源端口 → 服务端口，该行 owningPid 即浏览器。
    /// 先查 IPv4 表，再查 IPv6 表（现代浏览器对 localhost 常走 [::1]）。
    /// </summary>
    public static int GetClientPid(int clientSourcePort, int serverPort)
    {
        if (clientSourcePort <= 0 || serverPort <= 0) return 0;

        var pid = ScanTcpTable(AF_INET, (lp, rp) => lp == clientSourcePort && rp == serverPort);
        if (pid != 0) return pid;
        return ScanTcpTable(AF_INET6, (lp, rp) => lp == clientSourcePort && rp == serverPort);
    }

    private static int ScanTcpTable(int addressFamily, Func<int, int, bool> match)
    {
        int bufferSize = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, addressFamily, TCP_TABLE_OWNER_PID_ALL, 0);
        if (ret != 0 && ret != ERROR_INSUFFICIENT_BUFFER) return 0;

        IntPtr tablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            ret = GetExtendedTcpTable(tablePtr, ref bufferSize, false, addressFamily, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0) return 0;

            int numEntries = Marshal.ReadInt32(tablePtr);
            int rowSize = addressFamily == AF_INET6
                ? Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>()
                : Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            IntPtr rowPtr = tablePtr + 4;

            for (int i = 0; i < numEntries; i++)
            {
                IntPtr p = rowPtr + i * rowSize;
                int localPort;
                int remotePort;
                uint owningPid;

                if (addressFamily == AF_INET6)
                {
                    // MIB_TCP6ROW_OWNER_PID 布局：
                    // localAddr[16](0) localScopeId(16) localPort(20) remoteAddr[16](24)
                    // remoteScopeId(40) remotePort(44) state(48) owningPid(52)
                    localPort = (int)ntohs(ReadUInt16Le(p, 20));
                    remotePort = (int)ntohs(ReadUInt16Le(p, 44));
                    owningPid = ReadUInt32Le(p, 52);
                }
                else
                {
                    // MIB_TCPROW_OWNER_PID 布局：state(0) localAddr(4) localPort(8)
                    // remoteAddr(12) remotePort(16) owningPid(20)
                    localPort = (int)ntohs(ReadUInt16Le(p, 8));
                    remotePort = (int)ntohs(ReadUInt16Le(p, 16));
                    owningPid = ReadUInt32Le(p, 20);
                }

                if (match(localPort, remotePort))
                    return (int)owningPid;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    private static uint ntohs(ushort net)
    {
        return (uint)(((net & 0xFF) << 8) | ((net >> 8) & 0xFF));
    }

    /// <summary>小端读 UInt16（Marshal.ReadUInt16 在当前 SDK 不可用）。</summary>
    private static ushort ReadUInt16Le(IntPtr p, int offset)
    {
        return (ushort)(Marshal.ReadByte(p, offset) | (Marshal.ReadByte(p, offset + 1) << 8));
    }

    /// <summary>小端读 UInt32。</summary>
    private static uint ReadUInt32Le(IntPtr p, int offset)
    {
        return (uint)(Marshal.ReadByte(p, offset)
            | (Marshal.ReadByte(p, offset + 1) << 8)
            | (Marshal.ReadByte(p, offset + 2) << 16)
            | (Marshal.ReadByte(p, offset + 3) << 24));
    }

    // ---------- 进程信息 ----------

    public static string? GetProcessName(int pid)
    {
        if (pid <= 0) return null;
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName; // 不含 .exe
        }
        catch
        {
            return null;
        }
    }

    /// <summary>进程 → 可执行文件完整路径（如 D:\Tool\Doubao\app\Doubao.exe）。失败返回 null。</summary>
    public static string? GetProcessExecutablePath(int pid)
    {
        if (pid <= 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (QueryFullProcessImageName(h, 0, sb, ref size))
                return sb.ToString();
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>按「exe 路径 → 进程名」两级证据解析浏览器名。与内核版本、UA 无关。</summary>
    public static string ResolveBrowserName(string? exePath, string? processName)
    {
        var fromPath = ToBrowserDisplayNameByPath(exePath);
        if (!string.IsNullOrEmpty(fromPath)) return fromPath;
        return ToBrowserDisplayNameByProcessName(processName);
    }

    /// <summary>exe 完整路径 → 浏览器名。路径含品牌安装目录，最可靠；返回 null 表示无法判定。</summary>
    private static string? ToBrowserDisplayNameByPath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var n = exePath.Trim().ToLowerInvariant();

        // 豆包浏览器（字节）：安装目录名通常为 Doubao
        if (n.Contains("\\doubao\\") || n.Contains("\\doubao browser\\") || n.EndsWith("\\doubao.exe"))
            return "豆包浏览器";

        if (n.Contains("\\microsoft\\edge\\") || n.EndsWith("\\msedge.exe") || n.EndsWith("\\msedgewebview2.exe"))
            return "Edge";

        if (n.Contains("\\google\\chrome\\") || n.EndsWith("\\chrome.exe"))
            return "Chrome";

        if (n.Contains("mozilla firefox") || n.EndsWith("\\firefox.exe"))
            return "Firefox";

        if (n.Contains("\\bravesoftware\\") || n.EndsWith("\\brave.exe"))
            return "Brave";

        if (n.Contains("\\opera\\") || n.EndsWith("\\opera.exe") || n.EndsWith("\\opera_autoupdate.exe"))
            return "Opera";

        if (n.Contains("vivaldi") || n.EndsWith("\\vivaldi.exe"))
            return "Vivaldi";

        if (n.EndsWith("\\iexplore.exe"))
            return "IE";

        // 国产 Chromium 壳（按安装目录/文件名关键词）
        if (n.Contains("quark")) return "夸克";
        if (n.Contains("qqbrowser") || n.EndsWith("\\qqbrowser.exe")) return "QQ浏览器";
        if (n.Contains("sogou")) return "搜狗浏览器";
        if (n.Contains("liebao") || n.Contains("lbbrowser")) return "猎豹浏览器";
        if (n.Contains("maxthon")) return "傲游浏览器";
        if (n.Contains("360se") || n.Contains("360chrome") || n.Contains("se360")) return "360浏览器";

        return null; // 路径不认识，交给进程名
    }

    /// <summary>进程名 → 友好浏览器名（兜底）。</summary>
    public static string ToBrowserDisplayNameByProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "未知浏览器";
        var n = processName.Trim().ToLowerInvariant();

        // 精确/前缀匹配，避免 chrome.exe 误匹配 chrome_crashpad
        if (n is "msedge" or "msedgewebview2") return "Edge";
        if (n is "chrome") return "Chrome";
        if (n is "firefox") return "Firefox";
        if (n is "brave") return "Brave";
        if (n is "opera" or "opera_autoupdate") return "Opera";
        if (n is "vivaldi") return "Vivaldi";
        if (n is "iexplore") return "IE";

        // 豆包浏览器（字节）：常见进程名
        if (n.Contains("doubao") || n.Contains("dbbrowser") || n.StartsWith("bytedance"))
            return "豆包浏览器";

        // 夸克 / 360 / QQ / 搜狗 等国产壳
        if (n.Contains("quark")) return "夸克";
        if (n.Contains("360") || n.Contains("se360")) return "360浏览器";
        if (n.Contains("qqbrowser")) return "QQ浏览器";
        if (n.Contains("sogou")) return "搜狗浏览器";
        if (n.Contains("liebao") || n.Contains("lbbrowser")) return "猎豹浏览器";
        if (n.Contains("maxthon")) return "傲游浏览器";

        return processName; // 原样返回，便于排查
    }

    /// <summary>兼容旧调用方：按进程名解析（保留原方法名语义）。</summary>
    public static string ToBrowserDisplayName(string? processName)
        => ToBrowserDisplayNameByProcessName(processName);

    public static string ResolveClientBrowser(int clientSourcePort, int serverPort)
    {
        var pid = GetClientPid(clientSourcePort, serverPort);
        var exe = GetProcessExecutablePath(pid);
        var proc = GetProcessName(pid);
        var name = ResolveBrowserName(exe, proc);
        Log.Information("浏览器进程识别: 源端口={Port} → pid={Pid} exe={Exe} process={Proc} → {Name}",
            clientSourcePort, pid, exe ?? "?", proc ?? "?", name);
        return name;
    }
}
