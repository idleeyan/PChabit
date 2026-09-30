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

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        TcpEstatsType estatsType,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        out TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize,
        IntPtr raw,
        uint rawVersion,
        uint rawSize);

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
}
