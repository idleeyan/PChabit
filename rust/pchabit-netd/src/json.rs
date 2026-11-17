//! 最小 JSON 输出与转义（零依赖，载荷固定且字段名短，避免引入 serde）。

use std::fmt::Write as _;

pub fn escape(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 2);
    for ch in s.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => {
                let _ = write!(out, "\\u{:04x}", c as u32);
            }
            c => out.push(c),
        }
    }
    out
}

pub struct Obj {
    buf: String,
    first: bool,
    closed: bool,
}

impl Obj {
    pub fn new() -> Self {
        Self {
            buf: String::from("{"),
            first: true,
            closed: false,
        }
    }

    fn sep(&mut self) {
        if !self.first {
            self.buf.push(',');
        }
        self.first = false;
    }

    pub fn num(&mut self, key: &str, v: i64) -> &mut Self {
        self.sep();
        let _ = write!(self.buf, "\"{}\":{}", key, v);
        self
    }

    pub fn fnum(&mut self, key: &str, v: f64) -> &mut Self {
        self.sep();
        // 非有限值一律写 0，避免输出 NaN/Inf 破坏解析
        let value = if v.is_finite() { v } else { 0.0 };
        let _ = write!(self.buf, "\"{}\":{:.3}", key, value);
        self
    }

    pub fn boolean(&mut self, key: &str, v: bool) -> &mut Self {
        self.sep();
        let _ = write!(self.buf, "\"{}\":{}", key, v);
        self
    }

    pub fn string(&mut self, key: &str, v: &str) -> &mut Self {
        self.sep();
        let _ = write!(self.buf, "\"{}\":\"{}\"", key, escape(v));
        self
    }

    pub fn raw(&mut self, key: &str, v: &str) -> &mut Self {
        self.sep();
        let _ = write!(self.buf, "\"{}\":{}", key, v);
        self
    }

    pub fn finish(&mut self) -> String {
        if !self.closed {
            self.buf.push('}');
            self.closed = true;
        }
        self.buf.clone()
    }
}

/// 数组元素拼接（元素已是 JSON 片段）。
pub fn array(items: &[String]) -> String {
    let mut out = String::with_capacity(2 + items.iter().map(|s| s.len() + 1).sum::<usize>());
    out.push('[');
    for (i, it) in items.iter().enumerate() {
        if i > 0 {
            out.push(',');
        }
        out.push_str(it);
    }
    out.push(']');
    out
}
