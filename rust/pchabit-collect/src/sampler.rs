//! 本地硬件采样：CPU 占用（GetSystemTimes 增量）、内存（GlobalMemoryStatusEx）。
//!
//! GPU 温度/占用与网络速率不在这里取——netd 已经在 1 Hz 采，宿主要做的是
//! 把两边的读数合并进同一个「硬件分钟样本」。

use crate::win::{FileTime, GlobalMemoryStatusEx, GetSystemTimes, MemStatus};

#[derive(Default, Clone, Copy)]
pub struct CpuSample {
    pub load: f64,
}

pub struct CpuSampler {
    prev_idle: u64,
    prev_kernel: u64,
    prev_user: u64,
    primed: bool,
}

impl CpuSampler {
    pub fn new() -> Self {
        Self {
            prev_idle: 0,
            prev_kernel: 0,
            prev_user: 0,
            primed: false,
        }
    }

    /// 返回 0~100 的占用率；第一次调用只建立基准，返回 None。
    pub fn tick(&mut self) -> Option<f64> {
        let mut idle = FileTime::default();
        let mut kernel = FileTime::default();
        let mut user = FileTime::default();
        let ok = unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) };
        if ok == 0 {
            return None;
        }
        let (i, k, u) = (idle.q(), kernel.q(), user.q());
        if !self.primed {
            self.prev_idle = i;
            self.prev_kernel = k;
            self.prev_user = u;
            self.primed = true;
            return None;
        }
        let di = i.wrapping_sub(self.prev_idle);
        let dk = k.wrapping_sub(self.prev_kernel);
        let du = u.wrapping_sub(self.prev_user);
        self.prev_idle = i;
        self.prev_kernel = k;
        self.prev_user = u;

        // kernel 时间包含 idle，所以总时间 = kernel + user
        let total = dk + du;
        if total == 0 {
            return None;
        }
        let busy = total.saturating_sub(di) as f64;
        let pct = (busy / total as f64) * 100.0;
        Some(pct.clamp(0.0, 100.0))
    }
}

#[derive(Default, Clone, Copy)]
pub struct MemSample {
    pub load: f64,
    pub used_mb: f64,
    pub total_mb: f64,
}

pub fn memory() -> Option<MemSample> {
    let mut m = MemStatus {
        dwLength: std::mem::size_of::<MemStatus>() as u32,
        ..Default::default()
    };
    let ok = unsafe { GlobalMemoryStatusEx(&mut m) };
    if ok == 0 {
        return None;
    }
    let total_mb = m.ullTotalPhys as f64 / (1024.0 * 1024.0);
    let used_mb = (m.ullTotalPhys.saturating_sub(m.ullAvailPhys)) as f64 / (1024.0 * 1024.0);
    Some(MemSample {
        load: m.dwMemoryLoad as f64,
        used_mb,
        total_mb,
    })
}
