//! 采样状态机：与 C# `ProcessNetworkMonitor` 的口径逐条对齐。
//!
//! 对齐清单（改动此处必须同步核对 C# 侧，否则统计口径会漂）：
//! 1. 连接数按 PID 统计 TCP 全部状态 + UDP（v4/v6）。
//! 2. 只有 ESTABLISHED 的 TCP 连接参与 ESTATS 字节归因，且只记「相对上拍基线的增量」。
//! 3. 系统级上/下行由宿主推送的速率 × 本拍 elapsed 积分（宿主侧 LHM 为权威口径）。
//! 4. 进程速率之和超过系统速率 105% 时按比例收缩（NormalizeRatesToSystem）。
//! 5. 今日累计按「进程名」聚合；跨天清零；启动时可由库内数据回填。
//! 6. 排除自身与系统伪进程；30 秒无连接无速率的进程状态回收；最多跟踪 256 个进程。

use std::collections::{HashMap, HashSet};
use std::time::{Duration, Instant};

use crate::iphelper::{self, ConnSnapshot};
use crate::procname;
const MAX_TRACKED_PROCESSES: usize = 256;
const PRUNE_AFTER: Duration = Duration::from_secs(30);
/// 单条连接单拍增量上限：超过视为基线异常（连接复用/计数回绕），丢弃该增量。
const DELTA_SANITY_CAP: i64 = 500_000_000;

pub struct ProcState {
    pub pid: u32,
    pub name: String,
    pub conns: u32,
    pub rate_up: f64,
    pub rate_down: f64,
    pub session_up: i64,
    pub session_down: i64,
    last_active: Instant,
}

#[derive(Clone)]
pub struct RateRow {
    pub name: String,
    pub pid: u32,
    pub up_bps: f64,
    pub down_bps: f64,
    pub conns: u32,
    pub session_up: i64,
    pub session_down: i64,
}

#[derive(Clone)]
pub struct TodayRow {
    pub name: String,
    pub up: i64,
    pub down: i64,
}

pub struct TickInfo {
    pub elapsed_sec: f64,
    pub duration_ms: f64,
    pub conn_rows: usize,
    pub established: usize,
    pub estats_ok: usize,
    pub estats_bytes: i64,
}

pub struct NetState {
    procs: HashMap<u32, ProcState>,
    today: HashMap<String, TodayRow>,
    name_cache: HashMap<u32, String>,

    /// 每条连接的 ESTATS 累计基线，键为连接四元组。
    conn_last: HashMap<String, (u64, u64)>,
    /// 已尝试过启用连接级统计的连接键（每连接只付一次 syscall）。
    estats_attempted: HashSet<String>,
    estats_granted: Option<bool>,
    elevated: bool,

    sys_session_up: i64,
    sys_session_down: i64,
    sys_today_up: i64,
    sys_today_down: i64,
    /// 宿主推送的系统上下行字节增量（本拍实测值，未消费前累积）。
    pending_up: i64,
    pending_down: i64,
    display_up_bps: f64,
    display_down_bps: f64,

    day_key: String,
    last_tick: Instant,
    last_client: Instant,
    exclude_names: Vec<String>,
    self_pid: u32,

    pub tick_count: u64,
    pub last_tick_ms: f64,
    pub last_estats_bytes: i64,
    pub last_estats_ok: usize,
    pub last_conn_rows: usize,
}

impl NetState {
    pub fn new(exclude_names: Vec<String>) -> Self {
        Self {
            procs: HashMap::new(),
            today: HashMap::new(),
            name_cache: HashMap::new(),
            conn_last: HashMap::new(),
            estats_attempted: HashSet::new(),
            estats_granted: None,
            elevated: iphelper::is_elevated(),
            sys_session_up: 0,
            sys_session_down: 0,
            sys_today_up: 0,
            sys_today_down: 0,
            pending_up: 0,
            pending_down: 0,
            display_up_bps: 0.0,
            display_down_bps: 0.0,
            day_key: crate::timelocal::local_date_key(),
            last_tick: Instant::now(),
            last_client: Instant::now(),
            exclude_names,
            self_pid: std::process::id(),
            tick_count: 0,
            last_tick_ms: 0.0,
            last_estats_bytes: 0,
            last_estats_ok: 0,
            last_conn_rows: 0,
        }
    }

    /// 宿主推送系统级上下行「本拍字节增量」。
    /// 用增量而非速率：宿主与内核各有自己的 1 秒时钟，推速率再积分会错拍丢尖峰（实测低 15%）。
    pub fn add_system_delta(&mut self, up_bytes: i64, down_bytes: i64) {
        self.pending_up += up_bytes.max(0);
        self.pending_down += down_bytes.max(0);
    }

    pub fn seed_today(&mut self, name: &str, up: i64, down: i64) {
        if name.trim().is_empty() || (up <= 0 && down <= 0) || self.is_ignored(name) {
            return;
        }
        let b = self
            .today
            .entry(name.to_string())
            .or_insert(TodayRow { name: name.to_string(), up: 0, down: 0 });
        // 与 C# 一致：用库内值取 max，避免重启后数值倒退
        b.up = b.up.max(up);
        b.down = b.down.max(down);
    }

    pub fn seed_system(&mut self, up: i64, down: i64) {
        self.sys_today_up = self.sys_today_up.max(up);
        self.sys_today_down = self.sys_today_down.max(down);
    }

    /// 执行一拍采样。
    pub fn tick(&mut self) -> TickInfo {
        let started = Instant::now();
        let elapsed = started.duration_since(self.last_tick).as_secs_f64().clamp(0.05, 10.0);
        self.last_tick = started;
        self.roll_day_if_needed();

        let snap = iphelper::collect_connections();
        let conn_rows: usize = snap.conn_count.iter().map(|(_, n)| *n as usize).sum();

        // 1) 记录本拍连接数 + 维护进程表
        for &(pid, conns) in snap.conn_count.iter() {
            if pid == self.self_pid || pid == 0 || pid == u32::MAX {
                continue;
            }
            let known = self.procs.contains_key(&pid);
            let name = self.resolve_name(pid);
            if !known && self.procs.len() >= MAX_TRACKED_PROCESSES {
                continue;
            }
            if self.is_excluded_pid_name(&name) {
                continue;
            }
            let st = self.procs.entry(pid).or_insert(ProcState {
                pid,
                name: name.clone(),
                conns: 0,
                rate_up: 0.0,
                rate_down: 0.0,
                session_up: 0,
                session_down: 0,
                last_active: Instant::now(),
            });
            st.conns = conns;
            if st.name.is_empty() {
                st.name = name;
            }
        }

        // 2) ESTATS 归因：先清零本拍速率，再按真实增量赋值
        for st in self.procs.values_mut() {
            st.rate_up = 0.0;
            st.rate_down = 0.0;
        }

        let mut per_pid: HashMap<u32, (i64, i64)> = HashMap::new();
        let mut estats_ok = 0usize;
        let mut tick_bytes = 0i64;

        let ConnSnapshot {
            v4_established,
            v6_established,
            ..
        } = snap;

        for (pid, mut row) in v4_established {
            if pid == self.self_pid || self.is_excluded_pid(pid) {
                continue;
            }
            let key = key_v4(&row);
            self.maybe_enable_v4(&mut row, &key);
            let Some((cum_up, cum_down)) = iphelper::read_estats_v4(&mut row) else {
                continue;
            };
            estats_ok += 1;
            if let Some((d_up, d_down)) = self.diff(&key, cum_up, cum_down) {
                let e = per_pid.entry(pid).or_insert((0, 0));
                e.0 += d_up;
                e.1 += d_down;
                tick_bytes += d_up + d_down;
            }
        }

        for (pid, mut row) in v6_established {
            if pid == self.self_pid || self.is_excluded_pid(pid) {
                continue;
            }
            let key = key_v6(&row);
            self.maybe_enable_v6(&mut row, &key);
            let Some((cum_up, cum_down)) = iphelper::read_estats_v6(&mut row) else {
                continue;
            };
            estats_ok += 1;
            if let Some((d_up, d_down)) = self.diff(&key, cum_up, cum_down) {
                let e = per_pid.entry(pid).or_insert((0, 0));
                e.0 += d_up;
                e.1 += d_down;
                tick_bytes += d_up + d_down;
            }
        }

        // 清理消失连接的基线，防止 Map 无界增长
        if self.conn_last.len() > 512 {
            self.conn_last.retain(|k, _| self.estats_attempted.contains(k));
        }

        // 3) 写入进程速率 / 会话累计 / 今日累计
        let pid_names: Vec<(u32, String)> = per_pid
            .keys()
            .map(|p| (*p, self.procs.get(p).map(|s| s.name.clone()).unwrap_or_default()))
            .collect();

        for (pid, (d_up, d_down)) in per_pid.iter() {
            let name = pid_names
                .iter()
                .find(|(p, _)| p == pid)
                .map(|(_, n)| n.clone())
                .unwrap_or_default();

            if let Some(st) = self.procs.get_mut(pid) {
                if *d_up > 0 || *d_down > 0 {
                    st.rate_up = *d_up as f64 / elapsed;
                    st.rate_down = *d_down as f64 / elapsed;
                    st.session_up += *d_up;
                    st.session_down += *d_down;
                    st.last_active = Instant::now();
                }
            } else {
                continue;
            }

            if !name.is_empty() {
                self.add_today(&name, *pid, *d_up, *d_down);
            }
        }

        // 4) 系统级累计：直接叠加宿主推送的增量
        let sys_up_delta = std::mem::take(&mut self.pending_up);
        let sys_down_delta = std::mem::take(&mut self.pending_down);
        self.sys_session_up += sys_up_delta;
        self.sys_session_down += sys_down_delta;
        self.sys_today_up += sys_up_delta;
        self.sys_today_down += sys_down_delta;
        self.display_up_bps = sys_up_delta as f64 / elapsed;
        self.display_down_bps = sys_down_delta as f64 / elapsed;

        // 5) 进程速率不得显著超过系统速率
        self.normalize_to_system();

        // 6) 回收
        let now = Instant::now();
        self.procs.retain(|_, st| {
            let idle = now.duration_since(st.last_active);
            !(st.conns == 0 && st.rate_up <= 0.0 && st.rate_down <= 0.0 && idle > PRUNE_AFTER)
        });

        self.tick_count += 1;
        let duration_ms = started.elapsed().as_secs_f64() * 1000.0;
        self.last_tick_ms = duration_ms;
        self.last_estats_bytes = tick_bytes;
        self.last_estats_ok = estats_ok;
        self.last_conn_rows = conn_rows;

        TickInfo {
            elapsed_sec: elapsed,
            duration_ms,
            conn_rows,
            established: 0,
            estats_ok,
            estats_bytes: tick_bytes,
        }
    }

    fn normalize_to_system(&mut self) {
        let sum_up: f64 = self.procs.values().map(|s| s.rate_up.max(0.0)).sum();
        let sum_down: f64 = self.procs.values().map(|s| s.rate_down.max(0.0)).sum();

        let mut up_scale = 1.0;
        let mut down_scale = 1.0;
        if self.display_up_bps > 0.0 && sum_up > self.display_up_bps * 1.05 {
            up_scale = self.display_up_bps / sum_up;
        }
        if self.display_down_bps > 0.0 && sum_down > self.display_down_bps * 1.05 {
            down_scale = self.display_down_bps / sum_down;
        }
        if (up_scale - 1.0).abs() < 0.001 && (down_scale - 1.0).abs() < 0.001 {
            return;
        }
        for st in self.procs.values_mut() {
            if up_scale != 1.0 {
                st.rate_up *= up_scale;
            }
            if down_scale != 1.0 {
                st.rate_down *= down_scale;
            }
        }
    }

    fn add_today(&mut self, name: &str, pid: u32, up: i64, down: i64) {
        if up <= 0 && down <= 0 || self.is_ignored(name) {
            return;
        }
        let key = if name.trim().is_empty() {
            format!("pid:{pid}")
        } else {
            name.to_string()
        };
        let b = self
            .today
            .entry(key.clone())
            .or_insert(TodayRow { name: key, up: 0, down: 0 });
        b.up += up;
        b.down += down;
    }

    /// 计算单条连接的增量，并刷新基线；异常增量（负增长/超大）丢弃。
    fn diff(&mut self, key: &str, cum_up: u64, cum_down: u64) -> Option<(i64, i64)> {
        let prev = self.conn_last.insert(key.to_string(), (cum_up, cum_down));
        let Some((last_up, last_down)) = prev else {
            return None; // 首次见到该连接，仅建基线
        };

        let mut d_up = 0i64;
        let mut d_down = 0i64;
        if cum_up >= last_up {
            let d = (cum_up - last_up) as i64;
            if d > 0 && d < DELTA_SANITY_CAP {
                d_up = d;
            }
        }
        if cum_down >= last_down {
            let d = (cum_down - last_down) as i64;
            if d > 0 && d < DELTA_SANITY_CAP {
                d_down = d;
            }
        }
        if d_up == 0 && d_down == 0 {
            None
        } else {
            Some((d_up, d_down))
        }
    }

    fn maybe_enable_v4(&mut self, row: &mut iphelper::MibTcpRow, key: &str) {
        if self.estats_granted == Some(false) {
            return;
        }
        if self.estats_attempted.contains(key) {
            return;
        }
        if self.estats_attempted.len() > 1024 {
            self.estats_attempted.clear();
        }
        self.estats_attempted.insert(key.to_string());
        if self.estats_granted.is_none() && !self.elevated {
            self.estats_granted = Some(false);
            return;
        }
        let ret = iphelper::enable_estats_v4(row);
        self.apply_enable_result(ret, key);
    }

    fn maybe_enable_v6(&mut self, row: &mut iphelper::MibTcp6Row, key: &str) {
        if self.estats_granted == Some(false) {
            return;
        }
        if self.estats_attempted.contains(key) {
            return;
        }
        if self.estats_attempted.len() > 1024 {
            self.estats_attempted.clear();
        }
        self.estats_attempted.insert(key.to_string());
        if self.estats_granted.is_none() && !self.elevated {
            self.estats_granted = Some(false);
            return;
        }
        let ret = iphelper::enable_estats_v6(row);
        self.apply_enable_result(ret, key);
    }

    fn apply_enable_result(&mut self, ret: u32, key: &str) {
        match ret {
            iphelper::NO_ERROR => self.estats_granted = Some(true),
            iphelper::ERROR_ACCESS_DENIED => self.estats_granted = Some(false),
            _ => {
                // 连接在取样与启用之间关闭：不缓存结论，下拍重试
                self.estats_attempted.remove(key);
            }
        }
    }

    fn roll_day_if_needed(&mut self) {
        let key = crate::timelocal::local_date_key();
        if key == self.day_key {
            return;
        }
        self.day_key = key;
        self.today.clear();
        self.sys_today_up = 0;
        self.sys_today_down = 0;
    }

    fn resolve_name(&mut self, pid: u32) -> String {
        if let Some(n) = self.name_cache.get(&pid) {
            if !n.is_empty() {
                return n.clone();
            }
        }
        let name = procname::name_of(pid).unwrap_or_else(|| format!("pid:{pid}"));
        if self.name_cache.len() > 512 {
            self.name_cache.clear();
        }
        self.name_cache.insert(pid, name.clone());
        name
    }

    fn is_ignored(&self, name: &str) -> bool {
        if name.trim().is_empty() {
            return true;
        }
        if self.exclude_names.iter().any(|e| e.eq_ignore_ascii_case(name)) {
            return true;
        }
        ["Idle", "System", "Registry", "Memory Compression", "Secure System"]
            .iter()
            .any(|e| e.eq_ignore_ascii_case(name))
    }

    fn is_excluded_pid_name(&self, name: &str) -> bool {
        self.exclude_names.iter().any(|e| e.eq_ignore_ascii_case(name))
    }

    fn is_excluded_pid(&self, pid: u32) -> bool {
        let _ = pid;
        false
    }

    /// 标记一次客户端活动（宿主停摆时守护进程自动退出用）。
    pub fn touch_client(&mut self) {
        self.last_client = Instant::now();
    }

    /// 距上次客户端活动过去了多少秒。
    pub fn idle_secs(&self) -> f64 {
        self.last_client.elapsed().as_secs_f64()
    }

    pub fn is_elevated(&self) -> bool {
        self.elevated
    }

    pub fn estats_granted(&self) -> bool {
        self.estats_granted == Some(true)
    }

    /// 按速率 Top N（按进程名合并多 PID，与 C# `GetSnapshot` 同口径）。
    pub fn by_rate(&self, top: usize) -> Vec<RateRow> {
        let mut merged: HashMap<String, RateRow> = HashMap::new();
        for st in self.procs.values() {
            if self.is_ignored(&st.name) {
                continue;
            }
            let key = if st.name.trim().is_empty() {
                format!("pid:{}", st.pid)
            } else {
                st.name.clone()
            };
            let e = merged.entry(key.clone()).or_insert(RateRow {
                name: key,
                pid: st.pid,
                up_bps: 0.0,
                down_bps: 0.0,
                conns: 0,
                session_up: 0,
                session_down: 0,
            });
            e.up_bps += st.rate_up.max(0.0);
            e.down_bps += st.rate_down.max(0.0);
            e.session_up += st.session_up;
            e.session_down += st.session_down;
            e.conns += st.conns;
        }

        let mut rows: Vec<RateRow> = merged
            .into_values()
            .filter(|r| r.up_bps + r.down_bps > 0.01)
            .collect();
        rows.sort_by(|a, b| {
            (b.up_bps + b.down_bps)
                .partial_cmp(&(a.up_bps + a.down_bps))
                .unwrap_or(std::cmp::Ordering::Equal)
                .then_with(|| b.conns.cmp(&a.conns))
        });
        rows.truncate(top);
        rows
    }

    /// 今日累计 Top N。
    pub fn by_today(&self, top: usize) -> Vec<TodayRow> {
        let mut rows: Vec<TodayRow> = self
            .today
            .values()
            .filter(|r| !self.is_ignored(&r.name) && r.up + r.down > 0)
            .cloned()
            .collect();
        rows.sort_by(|a, b| (b.up + b.down).cmp(&(a.up + a.down)));
        rows.truncate(top);
        rows
    }

    /// 今日全部进程累计（不截断）。
    pub fn all_today(&self) -> Vec<TodayRow> {
        self.by_today(usize::MAX)
    }

    pub fn totals(&self) -> (i64, i64, i64, i64) {
        (self.sys_session_up, self.sys_session_down, self.sys_today_up, self.sys_today_down)
    }
}

fn key_v4(row: &iphelper::MibTcpRow) -> String {
    format!(
        "{}:{}>{}:{}",
        row.local_addr, row.local_port, row.remote_addr, row.remote_port
    )
}

fn key_v6(row: &iphelper::MibTcp6Row) -> String {
    format!(
        "6|{}:{}>{}:{}",
        u128::from_le_bytes(row.local_addr),
        row.local_port,
        u128::from_le_bytes(row.remote_addr),
        row.remote_port
    )
}
