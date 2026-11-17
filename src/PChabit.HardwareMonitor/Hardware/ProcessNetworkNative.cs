using System.Runtime.InteropServices;

namespace PChabit.HardwareMonitor.Hardware;

/// <summary>
/// Windows IP Helper 进程网络流量原生接口。
/// 数据源与任务管理器/资源监视器同族：GetExtendedTcp/UdpTable（连接+PID）+
/// GetPerTcpConnectionEStats（连接级字节累计）。
/// </summary>
internal static class ProcessNetworkNative
{
    internal const int AF_INET = 2;
    internal const int AF_INET6 = 23;

    internal enum TcpTableClass
    {
        TcpTableBasicListener,
        TcpTableBasicConnections,
        TcpTableBasicAll,
        TcpTableOwnerPidListener,
        TcpTableOwnerPidConnections,
        TcpTableOwnerPidAll,
        TcpTableOwnerModuleListener,
        TcpTableOwnerModuleConnections,
        TcpTableOwnerModuleAll
    }

    internal enum UdpTableClass
    {
        UdpTableBasic,
        UdpTableOwnerPid,
        UdpTableOwnerModule
    }

    internal enum TcpEstatsType
    {
        TcpConnectionEstatsSynOpts = 0,
        TcpConnectionEstatsData = 1,
        TcpConnectionEstatsSndCong = 2,
        TcpConnectionEstatsPath = 3,
        TcpConnectionEstatsRecv = 4,
        TcpConnectionEstatsObs = 5,
        TcpConnectionEstatsBandwidth = 6,
        TcpConnectionEstatsFineRtt = 7,
        TcpConnectionEstatsApp = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    /// <summary>MIB_TCP6ROW：52 字节（State+LocalAddr[16]+ScopeId+Port+RemoteAddr[16]+ScopeId+Port）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcp6Row
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
    }

    /// <summary>MIB_TCP6ROW_OWNER_PID：56 字节，OwningPid 位于偏移 52。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcp6RowOwnerPid
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint OwningPid;
    }

    // MIB_UDP6ROW_OWNER_PID：LocalAddr[16] + ScopeId + Port + OwningPid，共 28 字节
    // OwningPid 偏移 24；连接表按行读取时用 Marshal.ReadInt32。

    /// <summary>TCP_ESTATS_DATA_ROD_v0：连接累计数据字节（IP Helper）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TcpEstatsDataRodV0
    {
        public ulong DataBytesOut;
        public ulong DataSegmentsOut;
        public ulong DataBytesIn;
        public ulong DataSegmentsIn;
        public ulong SoftErrors;
        public ulong SoftErrorReason;
        public ulong SndUna;
        public ulong SndNxt;
        public ulong SndMax;
        public ulong ThruBytesAcked;
        public ulong RcvNxt;
        public ulong ThruBytesReceived;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        UdpTableClass tableClass,
        uint reserved);

    /// <summary>
    /// 读取 IPv4 TCP 连接级扩展统计。
    /// 参数必须严格按 Rw / Ros / Rod 三段传递（各 3 个参数）：
    /// 只有 Rod 段才是要读回的统计，把缓冲区放错段会导致调用返回 NO_ERROR 但数据恒为 0。
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        TcpEstatsType estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        out TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    /// <summary>
    /// 启用/关闭 IPv4 TCP 连接级统计。Windows 默认关闭采集，必须先用本函数
    /// 打开 TCP_ESTATS_DATA，否则 <see cref="GetPerTcpConnectionEStats"/> 虽返回
    /// NO_ERROR 但所有字节计数恒为 0。
    /// 限制：仅 Administrators 组成员调用成功，否则返回 ERROR_ACCESS_DENIED(5)。
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint SetPerTcpConnectionEStats(
        ref MibTcpRow row,
        TcpEstatsType estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    /// <summary>读取 IPv6 TCP 连接级扩展统计（参数分段规则同 IPv4）。</summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        TcpEstatsType estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        out TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    /// <summary>启用/关闭 IPv6 TCP 连接级统计（仅管理员可用）。</summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint SetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        TcpEstatsType estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters lpIoCounters);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint NO_ERROR = 0;
    /// <summary>ERROR_ACCESS_DENIED：非管理员调用 SetPerTcpConnectionEStats。</summary>
    internal const uint ERROR_ACCESS_DENIED = 5;
}
