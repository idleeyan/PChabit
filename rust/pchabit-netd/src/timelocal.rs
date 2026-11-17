//! 本地日期键（跨天清零用）。走 Win32 GetLocalTime，避免引入时间库依赖。

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct SystemTime {
    pub year: u16,
    pub month: u16,
    pub day_of_week: u16,
    pub day: u16,
    pub hour: u16,
    pub minute: u16,
    pub second: u16,
    pub milliseconds: u16,
}

extern "system" {
    fn GetLocalTime(out: *mut SystemTime);
}

pub fn local_now() -> SystemTime {
    let mut st = SystemTime::default();
    unsafe { GetLocalTime(&mut st) };
    st
}

/// "YYYY-MM-DD"，与 C# 侧 `DateTime.Now.ToString("yyyy-MM-dd")` 同格式。
pub fn local_date_key() -> String {
    let st = local_now();
    format!("{:04}-{:02}-{:02}", st.year, st.month, st.day)
}

/// 本地 "HH:MM:SS"，仅日志用。
pub fn local_time_text() -> String {
    let st = local_now();
    format!("{:02}:{:02}:{:02}", st.hour, st.minute, st.second)
}
