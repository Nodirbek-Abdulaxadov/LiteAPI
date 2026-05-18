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
//! | `LITEAPI_RUST_READ_TIMEOUT_SECS`  | `30`             | Per-request read timeout in seconds                            |
//! | `LITEAPI_RUST_IDLE_TIMEOUT_SECS`  | `15`             | Idle timeout between keep-alive requests in seconds            |
//! | `LITEAPI_RUST_MAX_HEADER_BYTES`   | `65_536`         | Reject requests whose request-line + headers exceed this (DoS) |

use std::env;
use std::ffi::CString;
use std::io::{BufRead, BufReader, Write};
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
const DEFAULT_IDLE_TIMEOUT_SECS: u64 = 15;
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
                // Disable Nagle on the accepted socket; tiny responses are
                // fine on loopback but Nagle interacts poorly with the
                // request-per-RTT keep-alive pattern below.
                let _ = stream.set_nodelay(true);

                let limiter = limiter.clone();
                let cfg = cfg.clone();
                acquire_permit(&limiter, cfg.max_concurrent);
                let permit = PermitGuard::new(limiter.clone(), cfg.max_concurrent);

                thread::spawn(move || {
                    // Permit is released when this guard drops, including on
                    // panic in dev builds (panic=abort in release terminates
                    // the process, also fine).
                    let _permit = permit;
                    serve_connection(stream, handler, free_bytes, &cfg);
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
    idle_timeout: Duration,
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
            idle_timeout: Duration::from_secs(
                parse_u64_env("LITEAPI_RUST_IDLE_TIMEOUT_SECS").unwrap_or(DEFAULT_IDLE_TIMEOUT_SECS),
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

/// Handles every request that comes in on a single TCP connection. The
/// connection is kept open across requests unless either side asks to close
/// or the idle timeout expires between two requests.
fn serve_connection(
    stream: TcpStream,
    handler: HandleRequestV2,
    free_bytes: FreeBytes,
    cfg: &ListenerConfig,
) {
    let remote_ip = stream
        .peer_addr()
        .ok()
        .map(|a| a.ip().to_string())
        .unwrap_or_else(|| "unknown".to_string());

    let mut writer = match stream.try_clone() {
        Ok(c) => c,
        Err(err) => {
            eprintln!("client(v2): try_clone failed: {err}");
            return;
        }
    };
    let mut reader = BufReader::new(stream);

    // The first request must arrive within read_timeout; subsequent requests
    // get the more generous idle_timeout.
    let mut next_timeout = cfg.read_timeout;

    loop {
        if let Err(err) = reader.get_ref().set_read_timeout(Some(next_timeout)) {
            eprintln!("client(v2): set_read_timeout failed: {err}");
            return;
        }

        match parse_request_v2(&mut reader, cfg.max_body_bytes, cfg.max_header_bytes) {
            Ok(ParseOutcome::Ok(req)) => {
                let keep_alive = req.keep_alive;
                match invoke_handler_v2(handler, free_bytes, &remote_ip, req) {
                    Ok(response_bytes) => {
                        if let Err(err) = writer.write_all(&response_bytes) {
                            eprintln!("client(v2): write failed: {err}");
                            return;
                        }
                        if let Err(err) = writer.flush() {
                            eprintln!("client(v2): flush failed: {err}");
                            return;
                        }
                    }
                    Err(err) => {
                        eprintln!("client(v2): handler failed: {err}");
                        return;
                    }
                }

                if !keep_alive {
                    return;
                }

                // Switch to the idle timeout so a quiet client gets reaped
                // sooner than a slow first-request client.
                next_timeout = cfg.idle_timeout;
            }
            Ok(ParseOutcome::Empty) => return,
            Ok(ParseOutcome::BodyTooLarge) => {
                let _ = write_413(&mut writer, cfg.max_body_bytes);
                return;
            }
            Ok(ParseOutcome::HeadersTooLarge) => {
                let _ = write_status(&mut writer, 431, "Request Header Fields Too Large",
                    "Header section exceeds configured limit.");
                return;
            }
            Err(err) => {
                // io::ErrorKind::WouldBlock / TimedOut → idle expiry, normal.
                let kind = err.kind();
                if kind != std::io::ErrorKind::WouldBlock
                    && kind != std::io::ErrorKind::TimedOut
                    && kind != std::io::ErrorKind::UnexpectedEof
                {
                    eprintln!("client(v2): request parse failed: {err}");
                }
                return;
            }
        }
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
    /// `true` when the request (and the negotiated HTTP version) wants the
    /// connection to be reused for the next request.
    keep_alive: bool,
}

enum ParseOutcome {
    Ok(RequestV2),
    Empty,
    BodyTooLarge,
    HeadersTooLarge,
}

fn parse_request_v2<R: BufRead>(
    reader: &mut R,
    max_body_bytes: usize,
    max_header_bytes: usize,
) -> std::io::Result<ParseOutcome> {
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
    let http_version = parts.next().unwrap_or("HTTP/1.1");
    if method.is_empty() {
        return Ok(ParseOutcome::Empty);
    }

    // HTTP/1.0 defaults to close, HTTP/1.1 defaults to keep-alive — the
    // Connection header on the request can flip the default either way.
    let mut keep_alive = !http_version.eq_ignore_ascii_case("HTTP/1.0");

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
        } else if let Some(value) = lower.strip_prefix("connection:") {
            let v = value.trim();
            if v.eq_ignore_ascii_case("close") {
                keep_alive = false;
            } else if v.eq_ignore_ascii_case("keep-alive") {
                keep_alive = true;
            }
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
        keep_alive,
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
