//! Windows IP Helper 原生绑定（手写 FFI，零第三方依赖）。
//!
//! 对应 C# 侧 `ProcessNetworkNative.cs` 的契约，结构体尺寸与字段顺序逐字节对齐：
//! MIB_TCPROW_OWNER_PID(24) / MIB_TCP6ROW_OWNER_PID(56) /
//! MIB_UDPROW_OWNER_PID(12) / MIB_UDP6ROW_OWNER_PID(28) /
//! MIB_TCPROW(20) / MIB_TCP6ROW(52) / TCP_ESTATS_DATA_ROD_v0(96)。

use std::ffi::c_void;
use std::os::raw::c_int;
use std::ptr;

pub const AF_INET: u32 = 2;
pub const AF_INET6: u32 = 23;

pub const TCP_TABLE_OWNER_PID_ALL: u32 = 5;
pub const UDP_TABLE_OWNER_PID: u32 = 1;
pub const TCP_CONNECTION_ESTATS_DATA: u32 = 1;

pub const NO_ERROR: u32 = 0;
pub const ERROR_INSUFFICIENT_BUFFER: u32 = 122;
pub const ERROR_ACCESS_DENIED: u32 = 5;

/// MIB_TCP_STATE: ESTABLISHED == 5
pub const TCP_STATE_ESTABLISHED: u32 = 5;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct MibTcpRowOwnerPid {
    pub state: u32,
    pub local_addr: u32,
    pub local_port: u32,
    pub remote_addr: u32,
    pub remote_port: u32,
    pub owning_pid: u32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct MibTcp6RowOwnerPid {
    pub state: u32,
    pub local_addr: [u8; 16],
    pub local_scope_id: u32,
    pub local_port: u32,
    pub remote_addr: [u8; 16],
    pub remote_scope_id: u32,
    pub remote_port: u32,
    pub owning_pid: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct MibUdpRowOwnerPid {
    pub local_addr: u32,
    pub local_port: u32,
    pub owning_pid: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct MibUdp6RowOwnerPid {
    pub local_addr: [u8; 16],
    pub local_scope_id: u32,
    pub local_port: u32,
    pub owning_pid: u32,
}

/// ESTATS 接口使用的不带 PID 的 20 字节行。
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct MibTcpRow {
    pub state: u32,
    pub local_addr: u32,
    pub local_port: u32,
    pub remote_addr: u32,
    pub remote_port: u32,
}

/// ESTATS 接口使用的不带 PID 的 52 字节行。
#[repr(C)]
#[derive(Clone, Copy)]
pub struct MibTcp6Row {
    pub state: u32,
    pub local_addr: [u8; 16],
    pub local_scope_id: u32,
    pub local_port: u32,
    pub remote_addr: [u8; 16],
    pub remote_scope_id: u32,
    pub remote_port: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct TcpEstatsDataRodV0 {
    pub data_bytes_out: u64,
    pub data_segments_out: u64,
    pub data_bytes_in: u64,
    pub data_segments_in: u64,
    pub soft_errors: u64,
    pub soft_error_reason: u64,
    pub snd_una: u64,
    pub snd_nxt: u64,
    pub snd_max: u64,
    pub thru_bytes_acked: u64,
    pub rcv_nxt: u64,
    pub thru_bytes_received: u64,
}

pub const TCP_ESTATS_DATA_ROD_SIZE: u32 = std::mem::size_of::<TcpEstatsDataRodV0>() as u32;

#[link(name = "iphlpapi")]
extern "system" {
    pub fn GetExtendedTcpTable(
        tcp_table: *mut c_void,
        size: *mut u32,
        sort: c_int,
        af: u32,
        table_class: u32,
        reserved: u32,
    ) -> u32;

    pub fn GetExtendedUdpTable(
        udp_table: *mut c_void,
        size: *mut u32,
        sort: c_int,
        af: u32,
        table_class: u32,
        reserved: u32,
    ) -> u32;

    pub fn GetPerTcpConnectionEStats(
        row: *mut MibTcpRow,
        estats_type: u32,
        rw: *mut u8,
        rw_version: u32,
        rw_size: u32,
        ros: *mut u8,
        ros_version: u32,
        ros_size: u32,
        rod: *mut TcpEstatsDataRodV0,
        rod_version: u32,
        rod_size: u32,
    ) -> u32;

    pub fn SetPerTcpConnectionEStats(
        row: *mut MibTcpRow,
        estats_type: u32,
        rw: *mut u8,
        rw_version: u32,
        rw_size: u32,
        offset: u32,
    ) -> u32;

    pub fn GetPerTcp6ConnectionEStats(
        row: *mut MibTcp6Row,
        estats_type: u32,
        rw: *mut u8,
        rw_version: u32,
        rw_size: u32,
        ros: *mut u8,
        ros_version: u32,
        ros_size: u32,
        rod: *mut TcpEstatsDataRodV0,
        rod_version: u32,
        rod_size: u32,
    ) -> u32;

    pub fn SetPerTcp6ConnectionEStats(
        row: *mut MibTcp6Row,
        estats_type: u32,
        rw: *mut u8,
        rw_version: u32,
        rw_size: u32,
        offset: u32,
    ) -> u32;
}

/// 连接表快照：把两个 AF 家族的 TCP/UDP 行统一成 PID 维度。
#[derive(Default)]
pub struct ConnSnapshot {
    /// 每个 PID 的连接数（TCP 全部状态 + UDP）。
    pub conn_count: Vec<(u32, u32)>,
    /// ESTABLISHED 的 IPv4 连接（带 PID，供 ESTATS 读取与归因）。
    pub v4_established: Vec<(u32, MibTcpRow)>,
    /// ESTABLISHED 的 IPv6 连接。
    pub v6_established: Vec<(u32, MibTcp6Row)>,
}

/// 一次完整的连接表枚举（与 C# `CollectConnectionStats` 等价）。
pub fn collect_connections() -> ConnSnapshot {
    let mut snap = ConnSnapshot::default();
    let mut counts: Vec<(u32, u32)> = Vec::with_capacity(256);

    collect_tcp4(&mut counts, &mut snap.v4_established);
    collect_tcp6(&mut counts, &mut snap.v6_established);
    collect_udp4(&mut counts);
    collect_udp6(&mut counts);

    counts.sort_unstable_by_key(|&(pid, _)| pid);
    // 合并同名 PID（各采集函数已按 PID 累加，这里只做去重收口）
    let mut merged: Vec<(u32, u32)> = Vec::with_capacity(counts.len());
    for (pid, n) in counts {
        match merged.last_mut() {
            Some((p, c)) if *p == pid => *c += n,
            _ => merged.push((pid, n)),
        }
    }
    snap.conn_count = merged;
    snap
}

fn bump(counts: &mut Vec<(u32, u32)>, pid: u32) {
    if let Some(entry) = counts.iter_mut().find(|(p, _)| *p == pid) {
        entry.1 += 1;
    } else {
        counts.push((pid, 1));
    }
}

/// 两段式取表：先取长度再取内容，缓冲区不足时按返回值重建（最多 3 次）。
unsafe fn fetch_table(
    mut call: impl FnMut(*mut c_void, *mut u32) -> u32,
) -> Option<Vec<u8>> {
    let mut size: u32 = 0;
    let first = call(ptr::null_mut(), &mut size);
    if first != ERROR_INSUFFICIENT_BUFFER || size == 0 {
        return None;
    }

    for _ in 0..3 {
        let mut buf = vec![0u8; size as usize];
        let ret = call(buf.as_mut_ptr() as *mut c_void, &mut size);
        if ret == NO_ERROR {
            return Some(buf);
        }
        if ret != ERROR_INSUFFICIENT_BUFFER {
            return None;
        }
        buf.clear();
    }

    None
}

fn collect_tcp4(counts: &mut Vec<(u32, u32)>, established: &mut Vec<(u32, MibTcpRow)>) {
    // 缓冲区对齐 8 字节，避免未对齐读取
    unsafe {
        let Some(buf) = fetch_table(|p, s| {
            GetExtendedTcpTable(p, s, 0, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0)
        }) else {
            return;
        };
        if buf.len() < 4 {
            return;
        }
        let count = u32::from_ne_bytes([buf[0], buf[1], buf[2], buf[3]]) as usize;
        let row_size = std::mem::size_of::<MibTcpRowOwnerPid>();
        let need = 4 + count * row_size;
        if buf.len() < need {
            return;
        }

        for i in 0..count {
            let off = 4 + i * row_size;
            let row: &MibTcpRowOwnerPid =
                &*(buf.as_ptr().add(off) as *const MibTcpRowOwnerPid);
            if row.owning_pid == 0 || row.owning_pid == u32::MAX {
                continue;
            }
            bump(counts, row.owning_pid);
            if row.state != TCP_STATE_ESTABLISHED {
                continue;
            }
            established.push((
                row.owning_pid,
                MibTcpRow {
                    state: row.state,
                    local_addr: row.local_addr,
                    local_port: row.local_port,
                    remote_addr: row.remote_addr,
                    remote_port: row.remote_port,
                },
            ));
        }
    }
}

fn collect_tcp6(counts: &mut Vec<(u32, u32)>, established: &mut Vec<(u32, MibTcp6Row)>) {
    unsafe {
        let Some(buf) = fetch_table(|p, s| {
            GetExtendedTcpTable(p, s, 0, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0)
        }) else {
            return;
        };
        if buf.len() < 4 {
            return;
        }
        let count = u32::from_ne_bytes([buf[0], buf[1], buf[2], buf[3]]) as usize;
        let row_size = std::mem::size_of::<MibTcp6RowOwnerPid>();
        let need = 4 + count * row_size;
        if buf.len() < need {
            return;
        }

        for i in 0..count {
            let off = 4 + i * row_size;
            let row: &MibTcp6RowOwnerPid =
                &*(buf.as_ptr().add(off) as *const MibTcp6RowOwnerPid);
            if row.owning_pid == 0 || row.owning_pid == u32::MAX {
                continue;
            }
            bump(counts, row.owning_pid);
            if row.state != TCP_STATE_ESTABLISHED {
                continue;
            }
            established.push((
                row.owning_pid,
                MibTcp6Row {
                    state: row.state,
                    local_addr: row.local_addr,
                    local_scope_id: row.local_scope_id,
                    local_port: row.local_port,
                    remote_addr: row.remote_addr,
                    remote_scope_id: row.remote_scope_id,
                    remote_port: row.remote_port,
                },
            ));
        }
    }
}

fn collect_udp4(counts: &mut Vec<(u32, u32)>) {
    unsafe {
        let Some(buf) = fetch_table(|p, s| {
            GetExtendedUdpTable(p, s, 0, AF_INET, UDP_TABLE_OWNER_PID, 0)
        }) else {
            return;
        };
        if buf.len() < 4 {
            return;
        }
        let count = u32::from_ne_bytes([buf[0], buf[1], buf[2], buf[3]]) as usize;
        let row_size = std::mem::size_of::<MibUdpRowOwnerPid>();
        if buf.len() < 4 + count * row_size {
            return;
        }
        for i in 0..count {
            let off = 4 + i * row_size;
            let row: &MibUdpRowOwnerPid = &*(buf.as_ptr().add(off) as *const MibUdpRowOwnerPid);
            if row.owning_pid == 0 {
                continue;
            }
            bump(counts, row.owning_pid);
        }
    }
}

fn collect_udp6(counts: &mut Vec<(u32, u32)>) {
    unsafe {
        let Some(buf) = fetch_table(|p, s| {
            GetExtendedUdpTable(p, s, 0, AF_INET6, UDP_TABLE_OWNER_PID, 0)
        }) else {
            return;
        };
        if buf.len() < 4 {
            return;
        }
        let count = u32::from_ne_bytes([buf[0], buf[1], buf[2], buf[3]]) as usize;
        let row_size = std::mem::size_of::<MibUdp6RowOwnerPid>();
        if buf.len() < 4 + count * row_size {
            return;
        }
        for i in 0..count {
            let off = 4 + i * row_size;
            let row: &MibUdp6RowOwnerPid = &*(buf.as_ptr().add(off) as *const MibUdp6RowOwnerPid);
            if row.owning_pid == 0 {
                continue;
            }
            bump(counts, row.owning_pid);
        }
    }
}

/// 读取一条 IPv4 连接的累计字节（Rod 段）。返回 (累计出, 累计入)。
pub fn read_estats_v4(row: &mut MibTcpRow) -> Option<(u64, u64)> {
    let mut rod = TcpEstatsDataRodV0::default();
    let ret = unsafe {
        GetPerTcpConnectionEStats(
            row,
            TCP_CONNECTION_ESTATS_DATA,
            ptr::null_mut(),
            0,
            0,
            ptr::null_mut(),
            0,
            0,
            &mut rod,
            0,
            TCP_ESTATS_DATA_ROD_SIZE,
        )
    };
    if ret != NO_ERROR {
        return None;
    }
    let out = rod.data_bytes_out.max(rod.thru_bytes_acked);
    let inn = rod.data_bytes_in.max(rod.thru_bytes_received);
    Some((out, inn))
}

/// 读取一条 IPv6 连接的累计字节。
pub fn read_estats_v6(row: &mut MibTcp6Row) -> Option<(u64, u64)> {
    let mut rod = TcpEstatsDataRodV0::default();
    let ret = unsafe {
        GetPerTcp6ConnectionEStats(
            row,
            TCP_CONNECTION_ESTATS_DATA,
            ptr::null_mut(),
            0,
            0,
            ptr::null_mut(),
            0,
            0,
            &mut rod,
            0,
            TCP_ESTATS_DATA_ROD_SIZE,
        )
    };
    if ret != NO_ERROR {
        return None;
    }
    let out = rod.data_bytes_out.max(rod.thru_bytes_acked);
    let inn = rod.data_bytes_in.max(rod.thru_bytes_received);
    Some((out, inn))
}

/// 启用 IPv4 连接的 TCP_ESTATS_DATA 采集（仅管理员成功）。
/// Windows 默认关闭连接级统计，不启用时 Get 调用会返回 NO_ERROR 但字节恒为 0。
pub fn enable_estats_v4(row: &mut MibTcpRow) -> u32 {
    let mut rw: [u8; 1] = [1];
    unsafe {
        SetPerTcpConnectionEStats(
            row,
            TCP_CONNECTION_ESTATS_DATA,
            rw.as_mut_ptr(),
            0,
            1,
            0,
        )
    }
}

/// 启用 IPv6 连接的连接级统计。
pub fn enable_estats_v6(row: &mut MibTcp6Row) -> u32 {
    let mut rw: [u8; 1] = [1];
    unsafe {
        SetPerTcp6ConnectionEStats(
            row,
            TCP_CONNECTION_ESTATS_DATA,
            rw.as_mut_ptr(),
            0,
            1,
            0,
        )
    }
}

/// 是否具备管理员权限（决定能否启用连接级统计）。
pub fn is_elevated() -> bool {
    use std::ffi::c_void;
    // OpenProcessToken + GetTokenInformation(TokenElevation) 的最简实现
    #[link(name = "advapi32")]
    extern "system" {
        fn OpenProcessToken(process: *mut c_void, access: u32, token: *mut *mut c_void) -> c_int;
        fn GetTokenInformation(
            token: *mut c_void,
            class: u32,
            info: *mut c_void,
            len: u32,
            ret_len: *mut u32,
        ) -> c_int;
        fn CloseHandle(h: *mut c_void) -> c_int;
        fn GetCurrentProcess() -> *mut c_void;
    }

    #[repr(C)]
    struct TokenElevation {
        token_is_elevated: u32,
    }

    const TOKEN_QUERY: u32 = 0x0008;
    const TOKEN_ELEVATION: u32 = 20;

    unsafe {
        let mut token: *mut c_void = ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return false;
        }
        let mut info = TokenElevation {
            token_is_elevated: 0,
        };
        let mut ret_len: u32 = 0;
        let ok = GetTokenInformation(
            token,
            TOKEN_ELEVATION,
            &mut info as *mut _ as *mut c_void,
            std::mem::size_of::<TokenElevation>() as u32,
            &mut ret_len,
        );
        CloseHandle(token);
        ok != 0 && info.token_is_elevated != 0
    }
}

const _: () = {
    assert!(std::mem::size_of::<MibTcpRowOwnerPid>() == 24);
    assert!(std::mem::size_of::<MibTcp6RowOwnerPid>() == 56);
    assert!(std::mem::size_of::<MibUdpRowOwnerPid>() == 12);
    assert!(std::mem::size_of::<MibUdp6RowOwnerPid>() == 28);
    assert!(std::mem::size_of::<MibTcpRow>() == 20);
    assert!(std::mem::size_of::<MibTcp6Row>() == 52);
    assert!(std::mem::size_of::<TcpEstatsDataRodV0>() == 96);
};
