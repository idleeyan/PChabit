//! 探针：量出「进程网络采样」三个阶段的真实耗时。
//!
//! 阶段划分与 C# `ProcessNetworkMonitor.SampleOnce()` 对齐：
//!   1) 连接表枚举（TCP v4/v6 + UDP v4/v6，两段式取表）
//!   2) 逐连接 ESTATS 读取（ESTABLISHED 的每条连接一次 syscall）
//!   3) PID → 进程名解析（C# 侧带 512 项缓存，这里同时量冷/热两种）

use std::time::{Duration, Instant};

use crate::iphelper;
use crate::procname;

struct Stats {
    label: &'static str,
    samples_us: Vec<u64>,
}

impl Stats {
    fn new(label: &'static str) -> Self {
        Self {
            label,
            samples_us: Vec::new(),
        }
    }

    fn push(&mut self, d: Duration) {
        self.samples_us.push(d.as_micros() as u64);
    }

    fn report(&self) {
        if self.samples_us.is_empty() {
            println!("{:<28} 无样本", self.label);
            return;
        }
        let n = self.samples_us.len();
        let sum: u64 = self.samples_us.iter().sum();
        let avg = sum / n as u64;
        let mut sorted = self.samples_us.clone();
        sorted.sort_unstable();
        let p50 = sorted[n / 2];
        let p99 = sorted[(n * 99 / 100).min(n - 1)];
        println!(
            "{:<28} 平均 {:>8.3} ms | p50 {:>8.3} ms | p99 {:>8.3} ms | 最大 {:>8.3} ms",
            self.label,
            avg as f64 / 1000.0,
            p50 as f64 / 1000.0,
            p99 as f64 / 1000.0,
            *sorted.last().unwrap() as f64 / 1000.0
        );
    }
}

pub fn run() {
    let elevated = iphelper::is_elevated();
    println!("=== PChabit 进程网络采样探针 ===");
    println!("管理员权限: {}", if elevated { "是（ESTATS 可启用）" } else { "否（ESTATS 不可启用，与当前 App 一致）" });
    println!("逻辑处理器: {}", std::thread::available_parallelism().map(|v| v.get()).unwrap_or(1));

    // ---- 预热一拍，避免首次分配/首次 syscall 的抖动污染样本 ----
    let _ = iphelper::collect_connections();

    let ticks = 40u32;
    let mut t_conn = Stats::new("1. 连接表枚举 (4 张表)");
    let mut t_estats = Stats::new("2. ESTATS 逐连接读取");
    let mut t_names = Stats::new("3. 全进程名解析(冷)");

    let mut conn_rows_max = 0usize;
    let mut established_max = 0usize;
    let mut pid_count = 0usize;

    for _ in 0..ticks {
        // 阶段 1
        let t0 = Instant::now();
        let snap = iphelper::collect_connections();
        t_conn.push(t0.elapsed());

        let rows: u32 = snap.conn_count.iter().map(|(_, n)| *n).sum();
        conn_rows_max = conn_rows_max.max(rows as usize);
        established_max = established_max.max(snap.v4_established.len() + snap.v6_established.len());

        // 阶段 2（读 ESTATS，不启用采集，等价于非管理员时的 C# 行为）
        let t1 = Instant::now();
        let mut v4 = snap.v4_established.clone();
        let mut v6 = snap.v6_established.clone();
        let mut ok_count = 0u32;
        for (_pid, row) in v4.iter_mut() {
            if iphelper::read_estats_v4(row).is_some() {
                ok_count += 1;
            }
        }
        for (_pid, row) in v6.iter_mut() {
            if iphelper::read_estats_v6(row).is_some() {
                ok_count += 1;
            }
        }
        t_estats.push(t1.elapsed());

        // 阶段 3（冷解析：每拍都重新解析全部 PID 名）
        let t2 = Instant::now();
        let pids = procname::enum_pids();
        pid_count = pid_count.max(pids.len());
        let mut resolved = 0u32;
        for &pid in &pids {
            if procname::name_of(pid).is_some() {
                resolved += 1;
            }
        }
        t_names.push(t2.elapsed());

        let _ = (ok_count, resolved);
    }

    t_conn.report();
    t_estats.report();
    t_names.report();

    let tick_total_us: u64 = t_conn.samples_us.iter().sum::<u64>()
        + t_estats.samples_us.iter().sum::<u64>()
        + t_names.samples_us.iter().sum::<u64>();
    let per_tick_us = tick_total_us / ticks as u64;
    println!();
    println!("--- 规模 ---");
    println!("连接行数(峰值): {conn_rows_max}   ESTABLISHED 连接(峰值): {established_max}   进程数(峰值): {pid_count}");
    println!(
        "--- 合计 ---\n每拍成本: {:.3} ms（不含进程名缓存，为最坏情况）",
        per_tick_us as f64 / 1000.0
    );
    println!(
        "若 1 Hz 采样: 占单核 {:.2}% / 占 {:?} 核整机 {:.3}%",
        per_tick_us as f64 / 1_000_000.0 * 100.0,
        std::thread::available_parallelism().map(|v| v.get()).unwrap_or(1),
        per_tick_us as f64 / 1_000_000.0 * 100.0
            / std::thread::available_parallelism().map(|v| v.get()).unwrap_or(1) as f64
    );

    // ---- 打印 Top 10 连接数进程，供与界面数值交叉核对 ----
    println!();
    println!("--- 连接数 Top 10（供与 App 界面交叉核对）---");
    let mut by_pid: Vec<(u32, u32)> = Vec::new();
    {
        let snap = iphelper::collect_connections();
        for &(pid, n) in &snap.conn_count {
            by_pid.push((pid, n));
        }
    }
    let mut named: Vec<(String, u32, u32)> = by_pid
        .iter()
        .map(|&(pid, n)| (procname::name_of(pid).unwrap_or_else(|| format!("pid:{pid}")), pid, n))
        .collect();
    named.sort_by(|a, b| b.2.cmp(&a.2));
    for (name, pid, n) in named.iter().take(10) {
        println!("  {:<24} pid={:<7} 连接数={}", name, pid, n);
    }

    // ---- 按进程名归并后的 Top 6（与 C# GetSnapshot 的同名合并口径一致）----
    println!();
    println!("--- 按进程名归并 Top 6 ---");
    let mut by_name: Vec<(String, u32)> = Vec::new();
    for (name, _, n) in named.iter() {
        if let Some(e) = by_name.iter_mut().find(|(k, _)| k == name) {
            e.1 += *n;
        } else {
            by_name.push((name.clone(), *n));
        }
    }
    by_name.sort_by(|a, b| b.1.cmp(&a.1));
    for (name, n) in by_name.iter().take(6) {
        println!("  {:<24} 连接数={}", name, n);
    }
}
