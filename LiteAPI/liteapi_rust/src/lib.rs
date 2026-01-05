use std::env;
use std::ffi::{CStr, CString};
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::os::raw::{c_char, c_int};
use std::sync::{Arc, Condvar, Mutex};
use std::thread;
use std::time::Duration;

#[derive(Debug)]
struct Request {
    method: String,
    path: String,
    body: String,
}

type HandleRequest = unsafe extern "C" fn(*const c_char, *const c_char, *const c_char) -> *mut c_char;
type FreeString = unsafe extern "C" fn(*mut c_char);

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

#[no_mangle]
pub extern "C" fn start_listener(handle_cb: Option<HandleRequest>, free_cb: Option<FreeString>) -> c_int {
    let Some(handler) = handle_cb else {
        eprintln!("start_listener: handler callback is null");
        return -1;
    };
    // Accept bare host:port; if a scheme is mistakenly provided, strip it.
    let raw_addr = env::var("LITEAPI_RUST_ADDR").unwrap_or_else(|_| "127.0.0.1:6080".to_string());
    let addr = raw_addr.trim_start_matches("http://").trim_start_matches("https://").to_string();

    let listener = match TcpListener::bind(&addr) {
        Ok(l) => {
            println!("LiteAPI.rs running on: {addr}");
            l
        }
        Err(err) => {
            eprintln!("start_listener: failed to bind {addr}: {err}");
            return -1;
        }
    };

    for stream in listener.incoming() {
        match stream {
            Ok(mut stream) => {
                let handler = handler;
                let free_cb = free_cb;
                thread::spawn(move || {
                    if let Err(err) = stream.set_read_timeout(Some(Duration::from_secs(5))) {
                        eprintln!("client: set_read_timeout failed: {err}");
                        return;
                    }

                    match parse_request(&mut stream) {
                        Ok(Some(req)) => {
                            let body = invoke_handler(handler, free_cb, req);
                            if let Err(err) = write_response(&mut stream, &body) {
                                eprintln!("client: write_response failed: {err}");
                            }
                        }
                        Ok(None) => {
                            let _ = write_response(&mut stream, "");
                        }
                        Err(err) => {
                            eprintln!("client: request parse failed: {err}");
                        }
                    }
                });
            }
            Err(err) => {
                eprintln!("start_listener: accept failed: {err}");
            }
        }
    }

    0
}

#[no_mangle]
pub extern "C" fn start_listener_v2(handle_cb: Option<HandleRequestV2>, free_cb: Option<FreeBytes>) -> c_int {
    let Some(handler) = handle_cb else {
        eprintln!("start_listener_v2: handler callback is null");
        return -1;
    };
    let Some(free_bytes) = free_cb else {
        eprintln!("start_listener_v2: free callback is null");
        return -1;
    };

    let raw_addr = env::var("LITEAPI_RUST_ADDR").unwrap_or_else(|_| "127.0.0.1:6080".to_string());
    let addr = raw_addr
        .trim_start_matches("http://")
        .trim_start_matches("https://")
        .to_string();

    let max_body_bytes = env::var("LITEAPI_RUST_MAX_BODY_BYTES")
        .ok()
        .and_then(|v| v.parse::<usize>().ok())
        .unwrap_or(0);

    let max_concurrent = env::var("LITEAPI_RUST_MAX_CONCURRENT")
        .ok()
        .and_then(|v| v.parse::<usize>().ok())
        .unwrap_or(0);

    let limiter: Arc<(Mutex<usize>, Condvar)> = Arc::new((Mutex::new(0usize), Condvar::new()));

    let listener = match TcpListener::bind(&addr) {
        Ok(l) => {
            println!("LiteAPI.rs(v2) running on: {addr}");
            l
        }
        Err(err) => {
            eprintln!("start_listener_v2: failed to bind {addr}: {err}");
            return -1;
        }
    };

    for stream in listener.incoming() {
        match stream {
            Ok(mut stream) => {
                let handler = handler;
                let free_bytes = free_bytes;
                let limiter = limiter.clone();
                let max_concurrent = max_concurrent;
                let max_body_bytes = max_body_bytes;

                // Backpressure: do not spawn unbounded threads under load.
                if max_concurrent > 0 {
                    let (lock, cvar) = &*limiter;
                    let mut active = lock.lock().unwrap();
                    while *active >= max_concurrent {
                        active = cvar.wait(active).unwrap();
                    }
                    *active += 1;
                }

                thread::spawn(move || {
                    if let Err(err) = stream.set_read_timeout(Some(Duration::from_secs(5))) {
                        eprintln!("client(v2): set_read_timeout failed: {err}");
                        release_permit(&limiter, max_concurrent);
                        return;
                    }

                    let remote_ip = stream
                        .peer_addr()
                        .ok()
                        .map(|a| a.ip().to_string())
                        .unwrap_or_else(|| "unknown".to_string());

                    match parse_request_v2(&mut stream, max_body_bytes) {
                        Ok(Some(req)) => {
                            if req.body_too_large {
                                let _ = write_413(&mut stream, max_body_bytes);
                                release_permit(&limiter, max_concurrent);
                                return;
                            }

                            match invoke_handler_v2(handler, free_bytes, &remote_ip, req) {
                                Ok(response_bytes) => {
                                    if let Err(err) = stream.write_all(&response_bytes) {
                                        eprintln!("client(v2): write failed: {err}");
                                    }
                                    let _ = stream.flush();
                                }
                                Err(err) => eprintln!("client(v2): handler failed: {err}"),
                            }
                        }
                        Ok(None) => {
                            // No request data. Close.
                        }
                        Err(err) => {
                            eprintln!("client(v2): request parse failed: {err}");
                        }
                    }

                    release_permit(&limiter, max_concurrent);
                });
            }
            Err(err) => {
                eprintln!("start_listener_v2: accept failed: {err}");
            }
        }
    }

    0
}

fn parse_request(stream: &mut TcpStream) -> std::io::Result<Option<Request>> {
    let mut reader = BufReader::new(stream);

    let mut request_line = String::new();
    if reader.read_line(&mut request_line)? == 0 {
        return Ok(None);
    }

    let mut parts = request_line.split_whitespace();
    let method = parts.next().unwrap_or("").to_string();
    let path = parts.next().unwrap_or("/").to_string();

    if method.is_empty() {
        return Ok(None);
    }

    let mut content_length: usize = 0;
    let mut line = String::new();

    loop {
        line.clear();
        let read = reader.read_line(&mut line)?;
        if read == 0 {
            break;
        }

        let trimmed = line.trim_end_matches(&['\r', '\n'][..]);
        if trimmed.is_empty() {
            break;
        }

        if let Some(value) = trimmed.strip_prefix("Content-Length:") {
            content_length = value.trim().parse::<usize>().unwrap_or(0);
        }
    }

    let mut body = String::new();
    if content_length > 0 {
        let mut body_bytes = vec![0u8; content_length];
        reader.read_exact(&mut body_bytes)?;
        body = String::from_utf8_lossy(&body_bytes).into_owned();
    }

    Ok(Some(Request { method, path, body }))
}

#[derive(Debug)]
struct RequestV2 {
    method: String,
    path: String,
    headers: String,
    body: Vec<u8>,
    body_too_large: bool,
}

fn parse_request_v2(stream: &mut TcpStream, max_body_bytes: usize) -> std::io::Result<Option<RequestV2>> {
    let mut reader = BufReader::new(stream);

    let mut request_line = String::new();
    if reader.read_line(&mut request_line)? == 0 {
        return Ok(None);
    }

    let mut parts = request_line.split_whitespace();
    let method = parts.next().unwrap_or("").to_string();
    let path = parts.next().unwrap_or("/").to_string();

    if method.is_empty() {
        return Ok(None);
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
        let headers = headers_lines.join("\n");
        return Ok(Some(RequestV2 {
            method,
            path,
            headers,
            body: Vec::new(),
            body_too_large: true,
        }));
    }

    let mut body = vec![0u8; content_length];
    if content_length > 0 {
        reader.read_exact(&mut body)?;
    }

    let headers = headers_lines.join("\n");
    Ok(Some(RequestV2 { method, path, headers, body, body_too_large: false }))
}

fn release_permit(limiter: &Arc<(Mutex<usize>, Condvar)>, max_concurrent: usize) {
    if max_concurrent == 0 {
        return;
    }

    let (lock, cvar) = &**limiter;
    let mut active = lock.lock().unwrap();
    if *active > 0 {
        *active -= 1;
    }
    cvar.notify_one();
}

fn write_413(stream: &mut TcpStream, max_body_bytes: usize) -> std::io::Result<()> {
    let body = format!("Request body exceeds limit ({} bytes).", max_body_bytes);
    let header = format!(
        "HTTP/1.1 413 Payload Too Large\r\nContent-Length: {}\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\n",
        body.as_bytes().len()
    );

    stream.write_all(header.as_bytes())?;
    stream.write_all(body.as_bytes())?;
    stream.flush()
}

fn invoke_handler(handler: HandleRequest, free_cb: Option<FreeString>, req: Request) -> String {
    let method_c = CString::new(req.method).unwrap_or_default();
    let path_c = CString::new(req.path).unwrap_or_default();
    let body_c = CString::new(req.body).unwrap_or_default();

    let raw_ptr = unsafe { handler(method_c.as_ptr(), path_c.as_ptr(), body_c.as_ptr()) };
    if raw_ptr.is_null() {
        return String::new();
    }

    let response = unsafe { CStr::from_ptr(raw_ptr) }.to_string_lossy().into_owned();

    if let Some(free) = free_cb {
        unsafe { free(raw_ptr); }
    }

    response
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

fn write_response(stream: &mut TcpStream, body: &str) -> std::io::Result<()> {
    let body_bytes = body.as_bytes();
    let header = format!(
        "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\n",
        body_bytes.len()
    );

    stream.write_all(header.as_bytes())?;
    stream.write_all(body_bytes)?;
    stream.flush()
}
