//! 极简 WebDAV 客户端（只支持 http，够本项目的局域网 NAS 用）。
//!
//! 为什么要自己写而不是拉 reqwest：这里只需要 PUT / GET / PROPFIND / DELETE 四个动作，
//! 而且地址是 `http://192.168.x.x:5005/...` 这种明文内网盘；为了它拉一整套
//! TLS + HTTP/2 栈会让二进制胖一圈，编译也慢。自己实现只需处理
//! 「请求行 + 头 + Content-Length / chunked 响应」这几件事。

use std::io::{Read, Write};
use std::net::TcpStream;
use std::time::Duration;

/// 一次 WebDAV 会话的连接参数。
pub struct Dav {
    host: String,
    port: u16,
    /// 形如 `/PChabit`（不带结尾斜杠）
    base: String,
    auth: Option<String>,
}

impl Dav {
    /// 从 `url`（`WebDAVUrl`）+ 用户名密码构造；不是 http 直接报错。
    pub fn new(url: &str, user: &str, pass: &str) -> Result<Dav, String> {
        let rest = url
            .strip_prefix("http://")
            .ok_or_else(|| format!("只支持 http:// 的 WebDAV 地址（当前 {url}）"))?;
        let (hostport, path) = match rest.find('/') {
            Some(i) => (&rest[..i], &rest[i..]),
            None => (rest, "/"),
        };
        let (host, port) = match hostport.rsplit_once(':') {
            Some((h, p)) => (h.to_string(), p.parse::<u16>().unwrap_or(80)),
            None => (hostport.to_string(), 80),
        };
        let base = path.trim_end_matches('/').to_string();
        let auth = if user.trim().is_empty() {
            None
        } else {
            Some(format!("Basic {}", base64(&format!("{user}:{pass}"))))
        };
        Ok(Dav {
            host,
            port,
            base,
            auth,
        })
    }

    fn send(
        &self,
        method: &str,
        rel: &str,
        extra: &[(&str, String)],
        body: Option<&[u8]>,
    ) -> Result<(u16, Vec<u8>), String> {
        let path = format!("{}/{}", self.base, rel.trim_start_matches('/'));
        let mut stream = TcpStream::connect((self.host.as_str(), self.port))
            .map_err(|e| format!("连不上 {}:{}（{e}）", self.host, self.port))?;
        let _ = stream.set_read_timeout(Some(Duration::from_secs(30)));
        let _ = stream.set_write_timeout(Some(Duration::from_secs(30)));
        let mut req = format!(
            "{method} {path} HTTP/1.1\r\nHost: {}:{}\r\nConnection: close\r\nUser-Agent: PChabit/{}\r\n",
            self.host,
            self.port,
            env!("CARGO_PKG_VERSION")
        );
        if let Some(a) = &self.auth {
            req.push_str(&format!("Authorization: {a}\r\n"));
        }
        for (k, v) in extra {
            req.push_str(&format!("{k}: {v}\r\n"));
        }
        if let Some(b) = body {
            req.push_str(&format!("Content-Length: {}\r\n", b.len()));
        }
        req.push_str("\r\n");
        stream
            .write_all(req.as_bytes())
            .map_err(|e| format!("写请求失败：{e}"))?;
        if let Some(b) = body {
            stream
                .write_all(b)
                .map_err(|e| format!("写 body 失败：{e}"))?;
        }
        let mut buf = Vec::new();
        stream
            .read_to_end(&mut buf)
            .map_err(|e| format!("读响应失败：{e}"))?;
        parse_response(&buf)
    }

    /// 上传一个文件（PUT）。
    pub fn put(&self, rel: &str, bytes: &[u8]) -> Result<u16, String> {
        self.send(
            "PUT",
            rel,
            &[("Content-Type", "application/octet-stream".into())],
            Some(bytes),
        )
        .map(|(code, _)| code)
    }

    /// 下载一个文件（GET），404 返回 `None`。
    pub fn get(&self, rel: &str) -> Result<Option<Vec<u8>>, String> {
        let (code, body) = self.send("GET", rel, &[], None)?;
        Ok(match code {
            200..=299 => Some(body),
            _ => None,
        })
    }

    /// 列一下目录里的文件名（PROPFIND Depth:1）。
    pub fn list(&self, rel: &str) -> Result<Vec<String>, String> {
        let dir = if rel.is_empty() {
            format!("{}/", self.base)
        } else {
            format!("{}/{}/", self.base, rel.trim_matches('/'))
        };
        let body = b"<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>";
        // list 直接用绝对路径发（`send` 会再加 base，这里绕开）
        let mut stream = TcpStream::connect((self.host.as_str(), self.port))
            .map_err(|e| format!("连不上 {}:{}（{e}）", self.host, self.port))?;
        let _ = stream.set_read_timeout(Some(Duration::from_secs(30)));
        let mut req = format!(
            "PROPFIND {dir} HTTP/1.1\r\nHost: {}:{}\r\nDepth: 1\r\nConnection: close\r\nContent-Type: application/xml\r\nContent-Length: {}\r\n",
            self.host,
            self.port,
            body.len()
        );
        if let Some(a) = &self.auth {
            req.push_str(&format!("Authorization: {a}\r\n"));
        }
        req.push_str("\r\n");
        stream
            .write_all(req.as_bytes())
            .and_then(|_| stream.write_all(body))
            .map_err(|e| format!("写 PROPFIND 失败：{e}"))?;
        let mut buf = Vec::new();
        stream
            .read_to_end(&mut buf)
            .map_err(|e| format!("读响应失败：{e}"))?;
        let (code, body) = parse_response(&buf)?;
        if !(200..=299).contains(&code) {
            return Err(format!("PROPFIND 返回 {code}"));
        }
        let text = String::from_utf8_lossy(&body).to_string();
        let mut names = Vec::new();
        for href in hrefs(&text) {
            let decoded = percent_decode(&href);
            let last = decoded
                .trim_end_matches('/')
                .rsplit('/')
                .next()
                .unwrap_or("")
                .to_string();
            if !last.is_empty() && !decoded.ends_with('/') {
                names.push(last);
            }
        }
        Ok(names)
    }

    /// 删一个文件（DELETE），404 也算成功。
    pub fn delete(&self, rel: &str) -> Result<(), String> {
        let (code, _) = self.send("DELETE", rel, &[], None)?;
        if (200..=299).contains(&code) || code == 404 {
            Ok(())
        } else {
            Err(format!("DELETE 返回 {code}"))
        }
    }
}

fn parse_response(buf: &[u8]) -> Result<(u16, Vec<u8>), String> {
    let sep = buf
        .windows(4)
        .position(|w| w == b"\r\n\r\n")
        .ok_or_else(|| "响应没有头部结束标记".to_string())?;
    let head = String::from_utf8_lossy(&buf[..sep]).to_string();
    let mut body = buf[sep + 4..].to_vec();
    let code: u16 = head
        .lines()
        .next()
        .and_then(|l| l.split_whitespace().nth(1))
        .and_then(|s| s.parse().ok())
        .ok_or_else(|| "响应状态行读不出状态码".to_string())?;
    let chunked = head.to_ascii_lowercase().contains("transfer-encoding: chunked");
    if chunked {
        body = dechunk(&body);
    }
    Ok((code, body))
}

/// 极简 chunked 解码（WebDAV 的 PROPFIND 常用）。
fn dechunk(raw: &[u8]) -> Vec<u8> {
    let mut out = Vec::new();
    let mut pos = 0;
    loop {
        let line_end = match raw[pos..].windows(2).position(|w| w == b"\r\n") {
            Some(i) => pos + i,
            None => break,
        };
        let size_txt = String::from_utf8_lossy(&raw[pos..line_end]).to_string();
        let size_txt = size_txt.split(';').next().unwrap_or("").trim().to_string();
        let Ok(size) = usize::from_str_radix(&size_txt, 16) else {
            break;
        };
        if size == 0 {
            break;
        }
        let start = line_end + 2;
        let end = (start + size).min(raw.len());
        out.extend_from_slice(&raw[start..end]);
        pos = end + 2;
        if pos >= raw.len() {
            break;
        }
    }
    out
}

fn hrefs(xml: &str) -> Vec<String> {
    let mut out = Vec::new();
    let lower = xml.to_ascii_lowercase();
    let mut from = 0;
    while let Some(i) = lower[from..].find("<d:href>").or_else(|| lower[from..].find("<href>")) {
        let open = from + i;
        let tag = if lower[open..].starts_with("<d:href>") { "<d:href>" } else { "<href>" };
        let start = open + tag.len();
        let close = if tag == "<d:href>" { "</d:href>" } else { "</href>" };
        let end = match lower[start..].find(close) {
            Some(j) => start + j,
            None => break,
        };
        out.push(xml[start..end].trim().to_string());
        from = end + close.len();
    }
    out
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = String::new();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Ok(v) = u8::from_str_radix(&s[i + 1..i + 3], 16) {
                out.push(v as char);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i] as char);
        i += 1;
    }
    out
}

/// base64（只用得上 Standard 编码，自己写省一个依赖）。
fn base64(input: &str) -> String {
    const TABLE: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let bytes = input.as_bytes();
    let mut out = String::new();
    for chunk in bytes.chunks(3) {
        let b0 = chunk[0];
        let b1 = *chunk.get(1).unwrap_or(&0);
        let b2 = *chunk.get(2).unwrap_or(&0);
        out.push(TABLE[(b0 >> 2) as usize] as char);
        out.push(TABLE[(((b0 & 0x03) << 4) | (b1 >> 4)) as usize] as char);
        out.push(if chunk.len() > 1 {
            TABLE[(((b1 & 0x0F) << 2) | (b2 >> 6)) as usize] as char
        } else {
            '='
        });
        out.push(if chunk.len() > 2 {
            TABLE[(b2 & 0x3F) as usize] as char
        } else {
            '='
        });
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn base64_basic() {
        assert_eq!(base64("user:pass"), "dXNlcjpwYXNz");
    }

    #[test]
    fn chunked_decodes() {
        let raw = b"4\r\nWiki\r\n5\r\npedia\r\n0\r\n\r\n";
        assert_eq!(dechunk(raw), b"Wikipedia");
    }

    #[test]
    fn hrefs_extracted() {
        let xml = "<d:response><d:href>/PChabit/a.db</d:href></d:response>";
        assert_eq!(hrefs(xml), vec!["/PChabit/a.db".to_string()]);
    }
}
