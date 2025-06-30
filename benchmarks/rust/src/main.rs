use rand::Rng;
use serde::Serialize;
use std::net::SocketAddr;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;

#[derive(Serialize)]
struct WeatherForecast {
    date: String,
    #[serde(rename = "temperatureC")]
    temperature_c: i32,
    #[serde(rename = "temperatureF")]
    temperature_f: i32,
    summary: String,
}

#[tokio::main]
async fn main() {
    let listener = TcpListener::bind("127.0.0.1:9055")
        .await
        .expect("Failed to bind TCP listener");
    println!("LiteAPI-Rust-TcpServer: http://localhost:9055");

    loop {
        match listener.accept().await {
            Ok((mut socket, addr)) => {
                tokio::spawn(async move {
                    if let Err(e) = handle_client(&mut socket, addr).await {
                        eprintln!("Error handling {}: {}", addr, e);
                    }
                });
            }
            Err(e) => {
                eprintln!("Failed to accept connection: {}", e);
            }
        }
    }
}

async fn handle_client(socket: &mut tokio::net::TcpStream, _addr: SocketAddr) -> Result<(), Box<dyn std::error::Error>> {
    let mut buffer = [0u8; 4096];
    let n = socket.read(&mut buffer).await?;
    if n == 0 {
        return Ok(());
    }

    let request = match std::str::from_utf8(&buffer[..n]) {
        Ok(req) => req,
        Err(e) => {
            eprintln!("Invalid UTF-8 in request: {}", e);
            return Ok(());
        }
    };

    if !request.starts_with("GET ") {
        return Ok(());
    }

    let (status_line, body) = if request.contains("/weatherforecast") {
        let forecasts = generate_forecast();
        match serde_json::to_string(&forecasts) {
            Ok(json) => ("HTTP/1.1 200 OK\r\n", json),
            Err(e) => {
                eprintln!("Serialization error: {}", e);
                (
                    "HTTP/1.1 500 Internal Server Error\r\n",
                    "{\"error\":\"Internal Server Error\"}".to_string(),
                )
            }
        }
    } else {
        (
            "HTTP/1.1 404 Not Found\r\n",
            "{\"error\":\"Not found\"}".to_string(),
        )
    };

    let headers = format!(
        "{}Content-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
        status_line,
        body.len()
    );

    socket.write_all(headers.as_bytes()).await?;
    socket.write_all(body.as_bytes()).await?;
    socket.shutdown().await?;
    Ok(())
}

fn generate_forecast() -> Vec<WeatherForecast> {
    let summaries = [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm",
        "Balmy", "Hot", "Sweltering", "Scorching",
    ];

    let mut rng = rand::thread_rng();
    let now = chrono::Local::now().date_naive();

    let mut forecasts = Vec::with_capacity(500);
    for i in 1..=500 {
        let temp_c = rng.gen_range(-20..55);
        forecasts.push(WeatherForecast {
            date: (now + chrono::Duration::days(i)).to_string(),
            temperature_c: temp_c,
            temperature_f: 32 + (temp_c as f64 / 0.5556) as i32,
            summary: summaries[rng.gen_range(0..summaries.len())].to_string(),
        });
    }
    forecasts
}