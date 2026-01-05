use std::env;
use std::ffi::{CStr, CString};
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::os::raw::{c_char, c_int};
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
