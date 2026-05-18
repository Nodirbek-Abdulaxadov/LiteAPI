//! Minimal HTTP/1.1 TCP listener that forwards each request into a managed
//! callback. Designed to be loaded as a `cdylib` by the C# host.
//!
//! ## ABI
//! The only exported entry point is [`start_listener_v2`]. The legacy v1
//! string-based ABI was removed in 0.2.0 — every C# host on master already
//! talks v2.
//!
//! ## Environment variables
//! | Name                              | Default          | Purpose                                                        |
//! | ---                               | ---              | ---                                                            |
//! | `LITEAPI_RUST_ADDR`               | `127.0.0.1:6080` | Bind address                                                   |
//! | `LITEAPI_RUST_MAX_CONCURRENT`     | `0` (unlimited)  | Backpressure: cap the number of in-flight requests             |
//! | `LITEAPI_RUST_MAX_BODY_BYTES`     | `0` (unlimited)  | Reject requests whose `Content-Length` exceeds this with 413   |
//! | `LITEAPI_RUST_READ_TIMEOUT_SECS`  | `30`             | Per-connection read timeout in seconds                         |
//! | `LITEAPI_RUST_MAX_HEADER_BYTES`   | `65_536`         | Reject requests whose request-line + headers exceed this (DoS) |

use std::env;
use std::ffi::CString;
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::os::raw::{c_char, c_int};
use std::sync::{Arc, Condvar, Mutex};
use std::thread;
use std::time::Duration;

type HandleRequestV2 = unsafe extern "C" fn(
    *const c_char,
    *const c_char,
    *const c_char,
    *const c_char,
    *const u8,
    usize,
    *mut usize,
) -> *mut u8;

type FreeBytes = unsafe extern "C" fn(*mut u8, usize);

const DEFAULT_READ_TIMEOUT_SECS: u64 = 30;
const DEFAULT_MAX_HEADER_BYTES: usize = 64 * 1024;

#[no_mangle]
pub extern "C" fn start_listener_v2(
    handle_cb: Option<HandleRequestV2>,
    free_cb: Option<FreeBytes>,
) -> c_int {
    let Some(handler) = handle_cb else {
        eprintln!("start_listener_v2: handler callback is null");
        return -1;
    };
    let Some(free_bytes) = free_cb else {
        eprintln!("start_listener_v2: free callback is null");
        return -1;
    };

    let cfg = ListenerConfig::from_env();

    let listener = match TcpListener::bind(&cfg.addr) {
        Ok(l) => {
            println!("LiteAPI.rs(v2) running on: {}", cfg.addr);
            l
        }
        Err(err) => {
            eprintln!("start_listener_v2: failed to bind {}: {err}", cfg.addr);
            return -1;
        }
    };

    let limiter: Arc<(Mutex<usize>, Condvar)> = Arc::new((Mutex::new(0usize), Condvar::new()));

    for stream in listener.incoming() {
        match stream {
            Ok(stream) => {
                let limiter = limiter.clone();
                let cfg = cfg.clone();
                acquire_permit(&limiter, cfg.max_concurrent);
                let _permit = PermitGuard::new(limiter.clone(), cfg.max_concurrent);

                thread::spawn(move || {
                    // Permit is released when this guard drops — even on panic,
                    // because `panic = abort` in release means the process
                    // terminates, while in dev the guard still runs.
                    let _permit = _permit;
                    serve_one(stream, handler, free_bytes, &cfg);
                });
            }
            Err(err) => eprintln!("start_listener_v2: accept failed: {err}"),
        }
    }

    0
}

#[derive(Clone)]
struct ListenerConfig {
    addr: String,
    max_concurrent: usize,
    max_body_bytes: usize,
    max_header_bytes: usize,
    read_timeout: Duration,
}

impl ListenerConfig {
    fn from_env() -> Self {
        let raw_addr = env::var("LITEAPI_RUST_ADDR").unwrap_or_else(|_| "127.0.0.1:6080".to_string());
        let addr = raw_addr
            .trim_start_matches("http://")
            .trim_start_matches("https://")
            .to_string();

        Self {
            addr,
            max_concurrent: parse_usize_env("LITEAPI_RUST_MAX_CONCURRENT").unwrap_or(0),
            max_body_bytes: parse_usize_env("LITEAPI_RUST_MAX_BODY_BYTES").unwrap_or(0),
            max_header_bytes: parse_usize_env("LITEAPI_RUST_MAX_HEADER_BYTES")
                .unwrap_or(DEFAULT_MAX_HEADER_BYTES),
            read_timeout: Duration::from_secs(
                parse_u64_env("LITEAPI_RUST_READ_TIMEOUT_SECS").unwrap_or(DEFAULT_READ_TIMEOUT_SECS),
            ),
        }
    }
}

fn parse_usize_env(name: &str) -> Option<usize> {
    env::var(name).ok().and_then(|v| v.parse().ok())
}

fn parse_u64_env(name: &str) -> Option<u64> {
    env::var(name).ok().and_then(|v| v.parse().ok())
}

fn serve_one(
    mut stream: TcpStream,
    handler: HandleRequestV2,
    free_bytes: FreeBytes,
    cfg: &ListenerConfig,
) {
    if let Err(err) = stream.set_read_timeout(Some(cfg.read_timeout)) {
        eprintln!("client(v2): set_read_timeout failed: {err}");
        return;
    }

    let remote_ip = stream
        .peer_addr()
        .ok()
        .map(|a| a.ip().to_string())
        .unwrap_or_else(|| "unknown".to_string());

    match parse_request_v2(&mut stream, cfg.max_body_bytes, cfg.max_header_bytes) {
        Ok(ParseOutcome::Ok(req)) => match invoke_handler_v2(handler, free_bytes, &remote_ip, req) {
            Ok(response_bytes) => {
                if let Err(err) = stream.write_all(&response_bytes) {
                    eprintln!("client(v2): write failed: {err}");
                }
                let _ = stream.flush();
            }
            Err(err) => eprintln!("client(v2): handler failed: {err}"),
        },
        Ok(ParseOutcome::BodyTooLarge) => {
            let _ = write_413(&mut stream, cfg.max_body_bytes);
        }
        Ok(ParseOutcome::HeadersTooLarge) => {
            let _ = write_status(&mut stream, 431, "Request Header Fields Too Large",
                "Header section exceeds configured limit.");
        }
        Ok(ParseOutcome::Empty) => { /* idle client — close */ }
        Err(err) => eprintln!("client(v2): request parse failed: {err}"),
    }
}

/// Backpressure: block accept until a permit is available.
fn acquire_permit(limiter: &Arc<(Mutex<usize>, Condvar)>, max_concurrent: usize) {
    if max_concurrent == 0 {
        return;
    }
    let (lock, cvar) = &**limiter;
    let mut active = match lock.lock() {
        Ok(g) => g,
        Err(poisoned) => poisoned.into_inner(),
    };
    while *active >= max_concurrent {
        active = match cvar.wait(active) {
            Ok(g) => g,
            Err(poisoned) => poisoned.into_inner(),
        };
    }
    *active += 1;
}

/// RAII permit: guarantees the permit is released even if the worker thread
/// panics (no permit leak under partial failure).
struct PermitGuard {
    limiter: Arc<(Mutex<usize>, Condvar)>,
    armed: bool,
}

impl PermitGuard {
    fn new(limiter: Arc<(Mutex<usize>, Condvar)>, max_concurrent: usize) -> Self {
        Self { limiter, armed: max_concurrent > 0 }
    }
}

impl Drop for PermitGuard {
    fn drop(&mut self) {
        if !self.armed { return; }
        let (lock, cvar) = &*self.limiter;
        let mut active = match lock.lock() {
            Ok(g) => g,
            Err(poisoned) => poisoned.into_inner(),
        };
        if *active > 0 { *active -= 1; }
        cvar.notify_one();
    }
}

#[derive(Debug)]
struct RequestV2 {
    method: String,
    path: String,
    headers: String,
    body: Vec<u8>,
}

enum ParseOutcome {
    Ok(RequestV2),
    Empty,
    BodyTooLarge,
    HeadersTooLarge,
}

fn parse_request_v2(
    stream: &mut TcpStream,
    max_body_bytes: usize,
    max_header_bytes: usize,
) -> std::io::Result<ParseOutcome> {
    let mut reader = BufReader::new(stream);
    let mut header_bytes_consumed: usize = 0;

    let mut request_line = String::new();
    let n = reader.read_line(&mut request_line)?;
    if n == 0 {
        return Ok(ParseOutcome::Empty);
    }
    header_bytes_consumed += n;
    if header_bytes_consumed > max_header_bytes {
        return Ok(ParseOutcome::HeadersTooLarge);
    }

    let mut parts = request_line.split_whitespace();
    let method = parts.next().unwrap_or("").to_string();
    let path = parts.next().unwrap_or("/").to_string();
    if method.is_empty() {
        return Ok(ParseOutcome::Empty);
    }

    let mut headers_lines: Vec<String> = Vec::new();
    let mut content_length: usize = 0;
    let mut line = String::new();

    loop {
        line.clear();
        let read = reader.read_line(&mut line)?;
        if read == 0 {
            break;
        }
        header_bytes_consumed += read;
        if header_bytes_consumed > max_header_bytes {
            return Ok(ParseOutcome::HeadersTooLarge);
        }

        let trimmed = line.trim_end_matches(&['\r', '\n'][..]);
        if trimmed.is_empty() {
            break;
        }
        headers_lines.push(trimmed.to_string());

        let lower = trimmed.to_ascii_lowercase();
        if let Some(value) = lower.strip_prefix("content-length:") {
            content_length = value.trim().parse::<usize>().unwrap_or(0);
        }
    }

    if max_body_bytes > 0 && content_length > max_body_bytes {
        return Ok(ParseOutcome::BodyTooLarge);
    }

    let mut body = vec![0u8; content_length];
    if content_length > 0 {
        reader.read_exact(&mut body)?;
    }

    Ok(ParseOutcome::Ok(RequestV2 {
        method,
        path,
        headers: headers_lines.join("\n"),
        body,
    }))
}

fn write_413(stream: &mut TcpStream, max_body_bytes: usize) -> std::io::Result<()> {
    write_status(stream, 413, "Payload Too Large",
        &format!("Request body exceeds limit ({} bytes).", max_body_bytes))
}

fn write_status(stream: &mut TcpStream, code: u16, reason: &str, body: &str) -> std::io::Result<()> {
    let header = format!(
        "HTTP/1.1 {code} {reason}\r\nContent-Length: {}\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\n",
        body.len()
    );
    stream.write_all(header.as_bytes())?;
    stream.write_all(body.as_bytes())?;
    stream.flush()
}

fn invoke_handler_v2(
    handler: HandleRequestV2,
    free_bytes: FreeBytes,
    remote_ip: &str,
    req: RequestV2,
) -> Result<Vec<u8>, String> {
    let method_c = CString::new(req.method).map_err(|e| e.to_string())?;
    let path_c = CString::new(req.path).map_err(|e| e.to_string())?;
    let headers_c = CString::new(req.headers).map_err(|e| e.to_string())?;
    let remote_ip_c = CString::new(remote_ip).map_err(|e| e.to_string())?;

    let mut out_len: usize = 0;
    let raw_ptr = unsafe {
        handler(
            method_c.as_ptr(),
            path_c.as_ptr(),
            headers_c.as_ptr(),
            remote_ip_c.as_ptr(),
            req.body.as_ptr(),
            req.body.len(),
            &mut out_len as *mut usize,
        )
    };

    if raw_ptr.is_null() {
        return Ok(Vec::new());
    }

    let slice = unsafe { std::slice::from_raw_parts(raw_ptr, out_len) };
    let bytes = slice.to_vec();

    unsafe {
        free_bytes(raw_ptr, out_len);
    }

    Ok(bytes)
}
